using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using AsyncPropagation.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AsyncPropagation.Shared
{
    internal class InvocationChainFinder
    {
        private readonly ISearchMethods _searchMethods;

        internal InvocationChainFinder(ISearchMethods searchMethods)
        {
            _searchMethods = searchMethods;
        }

        /// <summary>
        /// Collects all SyntaxNodes which requires transformation (callsites and methods declarations)
        /// </summary>
        /// <param name="solution">Solution</param>
        /// <param name="startMethod">Start method</param>
        /// <param name="token"></param>
        /// <returns>Set of nodes to change</returns>
        internal async Task<HashSet<INodeToChange<SyntaxNode>>> GetMethodCallsAsync(Solution solution, IMethodSymbol startMethod,
            CancellationToken token)
        {
            var methods = new Queue<IMethodSymbol>();
            var callerInfos = new HashSet<INodeToChange<SyntaxNode>>();
            methods.Enqueue(startMethod);
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            //This adhoc traversal algorithm definitely has some flaws, such as multiple visits of nodes and undefined ordering of visits.
            //Because of that our await-counting method for ToSync conversion is not robust, but hopefully it will work in most cases.
            
            while (methods.Count > 0)
            {
                var method = methods.Dequeue();
                if (visited.Contains(method))
                    continue;
                
                var methodDeclaration =
                    await Task.WhenAll(method.DeclaringSyntaxReferences.Select(reference =>
                        CreateMethodSignature(reference, solution, method.ContainingType.IsAbstract)));
                callerInfos.ReplaceRange(methodDeclaration);

                var finds = await SymbolFinder.FindCallersAsync(method, solution, token);
                foreach (var referencer in finds)
                {
                    var callingMethodSymbol = (IMethodSymbol)referencer.CallingSymbol;

                    var probableInterfaces = callingMethodSymbol.ContainingType
                        .AllInterfaces.Where(interf => interf.MemberNames.Contains(callingMethodSymbol.Name));
                    
                    callerInfos.ReplaceRange(await CollectInterfaceMethodsDeclarations(solution, probableInterfaces, callingMethodSymbol));
                    
                    // Push the method overriden
                    var methodOverride = callingMethodSymbol;
                    while (methodOverride != null && methodOverride.IsOverride && methodOverride.OverriddenMethod != null)
                    {
                        methods.Enqueue(methodOverride.OverriddenMethod);
                        methodOverride = methodOverride.OverriddenMethod;
                    }

                    if (callingMethodSymbol.MethodKind == MethodKind.StaticConstructor)
                    {
                        continue;
                    }
                    
                    var methodDeclarations =
                        await Task.WhenAll(referencer.CallingSymbol.DeclaringSyntaxReferences.Select(reference =>
                            CreateMethodSignature(reference, solution, method.ContainingType.IsAbstract)));
                    
                    
                    var methodCalls = await Task.WhenAll(referencer.Locations.Select(l =>
                        _searchMethods.CreateMethodCallAsync(solution, referencer.CallingSymbol, methodDeclarations.Select(m => m.Node), l))
                    );
                    callerInfos.ReplaceRange(methodCalls);

                    if (_searchMethods.ShouldSearchForCallers(callingMethodSymbol,
                        methodDeclarations.Select(m => m.Node)))
                    {
                        callerInfos.ReplaceRange(methodDeclarations);
                        methods.Enqueue(callingMethodSymbol);
                    }
                    else
                    {
                        callerInfos.ReplaceRange(methodDeclarations.Select(m =>
                        {
                            m.KeepUntouched = true;
                            return m;
                        }));
                    }
                }

                visited.Add(method);
            }

            return callerInfos;
        }

        private static async Task<IEnumerable<INodeToChange<SyntaxNode>>> CollectInterfaceMethodsDeclarations(Solution solution, IEnumerable<INamedTypeSymbol> probableInterfaces, IMethodSymbol callingMethodSymbol)
        {
            var nodesToChange = new List<INodeToChange<SyntaxNode>>();
            foreach (var probableInterface in probableInterfaces)
            {
                foreach (var interfaceMember in probableInterface.GetMembers(callingMethodSymbol.Name))
                {
                    if (!(interfaceMember is IMethodSymbol methodSymbol))
                        continue;
                    
                    var interfaceMemberImplementation = callingMethodSymbol.ContainingType.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol;
                     if (interfaceMemberImplementation == null)
                        continue;

                    if (SymbolEqualityComparer.Default.Equals(interfaceMemberImplementation.ContainingType, callingMethodSymbol.ContainingType) && SymbolEqualityComparer.Default.Equals(interfaceMemberImplementation, callingMethodSymbol))
                    {
                        var methodDeclarations =
                            await Task.WhenAll(interfaceMember.DeclaringSyntaxReferences.Select(reference =>
                                CreateMethodSignature(reference, solution, true)));
                        nodesToChange.AddRange(methodDeclarations);
                    }
                    else
                    {
                        if (HaveIntersectionInHierarchy(interfaceMemberImplementation, callingMethodSymbol) ||
                            HaveIntersectionInHierarchy(callingMethodSymbol, interfaceMemberImplementation))
                        {
                            var methodDeclarations =
                                await Task.WhenAll(interfaceMemberImplementation.DeclaringSyntaxReferences.
                                    Select(reference => CreateMethodSignature(reference, solution, false))
                                );
                            
                            var interfaceDeclarations =
                                await Task.WhenAll(interfaceMember.DeclaringSyntaxReferences.
                                    Select(reference => CreateMethodSignature(reference, solution, true))
                                );
                            
                            nodesToChange.AddRange(methodDeclarations);
                            nodesToChange.AddRange(interfaceDeclarations);
                        }
                    }
                }
            }

            return nodesToChange;
        }

        /// <summary>
        /// Looking for matching method in inheritance hierarchy
        /// </summary>
        /// <param name="first">First method (which will be reduced)</param>
        /// <param name="second">Fixed method</param>
        /// <returns></returns>
        private static bool HaveIntersectionInHierarchy(IMethodSymbol? first, IMethodSymbol second)
        {
            if (first == null)
                return false;
            
            if (SymbolEqualityComparer.Default.Equals(first, second))
                return true;

            return HaveIntersectionInHierarchy(first.OverriddenMethod, second);
        }

        private static async Task<MethodSignature> CreateMethodSignature(SyntaxReference reference, Solution solution,
            bool isInterfaceMember)
        {
            var location = reference.SyntaxTree.GetLocation(reference.Span);
            var doc = solution.GetDocument(location.SourceTree);
            if (doc == null)
                return MethodSignature.NullObject;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null)
                return MethodSignature.NullObject;
            
            var node = root.FindNode(location.SourceSpan)
                .AncestorsAndSelf()
                .OfType<MethodDeclarationSyntax>().First();
            
            if (node == null)
                return MethodSignature.NullObject;
            
            return new MethodSignature(doc, node, isInterfaceMember);
        }
    }
}