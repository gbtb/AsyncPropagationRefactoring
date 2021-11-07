using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AsyncPropagation
{
    public class InvocationChainFinder
    {
        private readonly ISearchMethods _searchMethods;

        public InvocationChainFinder(ISearchMethods searchMethods)
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
            var methods = new Stack<IMethodSymbol>();
            var callerInfos = new HashSet<INodeToChange<SyntaxNode>>();
            methods.Push(startMethod);
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            
            while (methods.Count > 0)
            {
                var method = methods.Pop();
                if (visited.Contains(method))
                    continue;
                
                var methodDeclaration =
                    await Task.WhenAll(method.DeclaringSyntaxReferences.Select(reference =>
                        CreateMethodSignature(reference, solution, method.ContainingType.IsAbstract)));
                callerInfos.AddRange(methodDeclaration);

                var finds = await SymbolFinder.FindCallersAsync(method, solution, token);
                foreach (var referencer in finds)
                {
                    var callingMethodSymbol = (IMethodSymbol)referencer.CallingSymbol;
                    if (_searchMethods.ShouldSearchForCallers(callingMethodSymbol))
                        methods.Push(callingMethodSymbol);

                    var probableInterfaces = callingMethodSymbol.ContainingType
                        .AllInterfaces.Where(interf => interf.MemberNames.Contains(callingMethodSymbol.Name));
                    
                    callerInfos.AddRange(await CollectInterfaceMethodsDeclarations(solution, probableInterfaces, callingMethodSymbol));
                    
                    // Push the method overriden
                    var methodOverride = callingMethodSymbol;
                    while (methodOverride != null && methodOverride.IsOverride && methodOverride.OverriddenMethod != null)
                    {
                        methods.Push(methodOverride.OverriddenMethod);
                        methodOverride = methodOverride.OverriddenMethod;
                    }

                    if (callingMethodSymbol.MethodKind == MethodKind.StaticConstructor)
                    {
                        continue;
                    }
                    
                    var methodDeclarations =
                        await Task.WhenAll(referencer.CallingSymbol.DeclaringSyntaxReferences.Select(reference =>
                            CreateMethodSignature(reference, solution, method.ContainingType.IsAbstract)));
                    callerInfos.AddRange(methodDeclarations);
                    var methodCalls = await Task.WhenAll(referencer.Locations.Select(l =>
                        CreateMethodCall(solution, methodDeclarations.Select(m => m.Node), l))
                    );
                    callerInfos.AddRange(methodCalls);
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

        private static async Task<MethodCall> CreateMethodCall(Solution solution, IEnumerable<MethodDeclarationSyntax> methodDeclarations, Location location)
        {
            var doc = solution.GetDocument(location.SourceTree);
            if (doc == null)
                return MethodCall.NullObject;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null)
                return MethodCall.NullObject;
                    
            var invocation = methodDeclarations.Where(decl => decl.FullSpan.Contains(location.SourceSpan))
            .Select(decl => (decl.FindNode(location.SourceSpan)
                .AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>().First(), decl)
            ).FirstOrDefault();
            
            if (invocation.Item1 == null)
                return MethodCall.NullObject;

            return new MethodCall(doc, invocation.Item1, invocation.decl);
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

    public interface ISearchMethods
    {
        bool ShouldSearchForCallers(IMethodSymbol callingMethodSymbol);
    }
}