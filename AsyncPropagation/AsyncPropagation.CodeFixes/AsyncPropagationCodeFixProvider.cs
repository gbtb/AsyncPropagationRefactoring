using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AsyncPropagation
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncPropagationCodeFixProvider)), Shared]
    public class AsyncPropagationCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(AsyncPropagationAnalyzer.DiagnosticId); }
        }

        
        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var solution = context.Document.Project.Solution;
            var diagnostic = context.Diagnostics.FirstOrDefault();
            if (diagnostic != null && solution != null)
            {
                var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var diagnosticSpan = diagnostic.Location.SourceSpan;
                var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().First();
                var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
                var methodSymbol = semanticModel.GetDeclaredSymbol(declaration);

                var callerInfos = await GetMethodCallsAsync(solution, methodSymbol, context.CancellationToken);
                var codeFix = new ToAsyncInvocationCodefix();
                if (callerInfos.Count > 0)
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: CodeFixResources.CodeFixTitle,
                            createChangedSolution: c => codeFix.ExecuteAsync(solution, callerInfos),
                            equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                        diagnostic);
            }
        }

        private static async Task<HashSet<INodeToChange<SyntaxNode>>> GetMethodCallsAsync(Solution solution, IMethodSymbol startMethod,
            CancellationToken contextCancellationToken)
        {
            var methods = new Stack<IMethodSymbol>();
            var callerInfos = new HashSet<INodeToChange<SyntaxNode>>();
            methods.Push(startMethod);
            
            
            while (methods.Count > 0)
            {
                var method = methods.Pop();
                var methodDeclaration =
                    await Task.WhenAll(method.DeclaringSyntaxReferences.Select(reference =>
                        CreateMethodSignature(reference, solution, method.ContainingType.IsAbstract)));
                callerInfos.AddRange(methodDeclaration);

                var finds = await SymbolFinder.FindCallersAsync(method, solution, contextCancellationToken);
                foreach (var referencer in finds)
                {
                    var callingMethodSymbol = (IMethodSymbol)referencer.CallingSymbol;
                    if (!callingMethodSymbol.IsAsync && callingMethodSymbol.ReturnType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks")
                        methods.Push(callingMethodSymbol);

                    var probableInterfaces = callingMethodSymbol.ContainingType
                        .AllInterfaces.Where(interf => interf.MemberNames.Contains(callingMethodSymbol.Name));
                    
                    callerInfos.AddRange(await CollectInterfaceMethodsDeclarations(solution, probableInterfaces, callingMethodSymbol));
                    
                    //var impl = callingMethodSymbol.ContainingType.FindImplementationForInterfaceMember(callingMethodSymbol);
                        //var overridenMethods = CollectOverridenMethods(callingMethodSymbol.OverriddenMethod);
                    
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
            }

            return callerInfos;
        }

        private static async Task<IEnumerable<INodeToChange<SyntaxNode>>> CollectInterfaceMethodsDeclarations(Solution solution, IEnumerable<INamedTypeSymbol> probableInterfaces, IMethodSymbol callingMethodSymbol)
        {
            var nodesToChange = new List<INodeToChange<SyntaxNode>>();
            foreach (var probableInterface in probableInterfaces)
            {
                foreach (var member in probableInterface.GetMembers(callingMethodSymbol.Name))
                {
                    if (!(member is IMethodSymbol methodSymbol))
                        continue;
                    
                    var impl = callingMethodSymbol.ContainingType.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol;
                    if (impl == null)
                        continue;

                    if (impl.ContainingType == callingMethodSymbol.ContainingType && impl.Equals(callingMethodSymbol))
                    {
                        var methodDeclarations =
                            await Task.WhenAll(member.DeclaringSyntaxReferences.Select(reference =>
                                CreateMethodSignature(reference, solution, true)));
                        nodesToChange.AddRange(methodDeclarations);
                    }
                    else
                    {
                        if (HaveIntersectionInHierarchy(impl, callingMethodSymbol) ||
                            HaveIntersectionInHierarchy(callingMethodSymbol, impl))
                        {
                            var methodDeclarations =
                                await Task.WhenAll(impl.DeclaringSyntaxReferences.Select(reference =>
                                    CreateMethodSignature(reference, solution, true)));
                            nodesToChange.AddRange(methodDeclarations);
                        }
                    }
                }
            }

            return nodesToChange;
        }

        private static bool HaveIntersectionInHierarchy(IMethodSymbol? first, IMethodSymbol second)
        {
            if (first == null)
                return false;
            
            if (first.Equals(second))
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

//         private static IEnumerable<ILocation> CollectOverridenMethods(IMethodSymbol overriddenMethod)
//         {
//             do
//             {
//                 foreach (var syntaxReference in overriddenMethod.DeclaringSyntaxReferences)
//                 {
//                     yield return new MethodSignature(
//                         syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span));
//                 }
//
// #pragma warning disable 8600
//                 overriddenMethod = overriddenMethod.OverriddenMethod;
// #pragma warning restore 8600
//             } while (overriddenMethod != null);
//             
//         }

        // private Task<Solution> PropagateAsyncToCallingMethods(Solution solution, GraphResults graphResults, IMethodSymbol startMethod, CancellationToken c)
        // {
        //     var typeTransformed = new HashSet<ITypeSymbol>();
        //     var visited = new HashSet<IMethodSymbol>();
        //     var methods = new Stack<IMethodSymbol>();
        //     methods.Push(startMethod);
        //
        //     while (methods.Count > 0)
        //     {
        //         var methodSymbol = methods.Pop();
        //         if (!visited.Add(methodSymbol))
        //         {
        //             continue;
        //         }
        //
        //         var asyncTypes = graphResults.MethodGraph[methodSymbol];
        //
        //         foreach (var asyncTypeSymbol in asyncTypes)
        //         {
        //             // The type has been already transformed, don't try to transform it
        //             if (!typeTransformed.Add(asyncTypeSymbol))
        //             {
        //                 continue;
        //             }
        //
        //             var callingClass = graphResults.ClassGraph[asyncTypeSymbol];
        //
        //             var typeDecl = (TypeDeclarationSyntax)asyncTypeSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        //
        //             foreach (var callingMethod in callingClass.MethodCalls)
        //             {
        //                 var methodModel = callingMethod.MethodSymbol;
        //                 var method = callingMethod.CallerMethod;
        //
        //                 //Console.WriteLine(method.ToFullString());
        //                 //Console.Out.Flush();
        //                 //method = method.TrackNodes(callingMethod.CallSites);
        //                 //var originalMethod = method;
        //
        //                 bool addCancellationToken = false;
        //
        //                 method = method.ReplaceNodes(callingMethod.CallSites, ComputeReplacementNode);
        //
        //                 // if (addCancellationToken)
        //                 // {
        //                 //     method = method.WithParameterList(method.ParameterList.AddParameters(
        //                 //         Parameter(Identifier("cancellationToken")).WithType(IdentifierName("CancellationToken")).NormalizeWhitespace()
        //                 //     ));
        //                 // }
        //
        //                 TypeSyntax asyncReturnType;
        //                 if (methodModel.ReturnsVoid)
        //                 {
        //                     asyncReturnType = IdentifierName("Task").WithTrailingTrivia(Space);
        //                 }
        //                 else
        //                 {
        //                     var trailingTrivia = method.ReturnType.GetTrailingTrivia();
        //
        //                     asyncReturnType = GenericName(
        //                             Identifier("Task"))
        //                         .WithTypeArgumentList(
        //                             TypeArgumentList(
        //                                 SingletonSeparatedList(method.ReturnType.WithoutTrailingTrivia()))).WithTrailingTrivia(trailingTrivia);
        //                 }
        //
        //                 method = method.WithReturnType(asyncReturnType);
        //
        //                 // Rename method with `Async` postfix
        //                 method = method.WithIdentifier(Identifier(method.Identifier.Text + "Async"));
        //
        //                 // Add async keyword to the method
        //                 method = method.WithModifiers(method.Modifiers.Add(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space)));
        //
        //                 typeDecl = typeDecl.AddMembers(method);//adding new member
        //                 typeDecl.WithMembers(typeDecl.Members.Remove(callingMethod.CallerMethod));//removing old member
        //
        //                 methods.Push(callingMethod.MethodSymbol);
        //             }
        //
        //             //Debug.Assert(typeDecl.Members.All(x => x is MethodDeclarationSyntax));
        //
        //             // Update namespace
        //             namespaces[callingClass.Namespace] = namespaces[callingClass.Namespace].AddMembers(typeDecl);
        //
        //             // work on method transforms
        //         }
        //
        //         //methodSymbol.ContainingType.
        //     }
        //     
        // }

        private SyntaxNode ComputeReplacementNode(InvocationExpressionSyntax callSite, InvocationExpressionSyntax r)
        {
            var leadingTrivia = callSite.GetLeadingTrivia();
            var newCallSite = callSite.WithLeadingTrivia(Space);

            switch (newCallSite.Expression)
            {
                case MemberBindingExpressionSyntax m:
                {
                    var newExpression = m.WithName(IdentifierName(m.Name.ToString() + "Async"));
                    newCallSite = newCallSite.WithExpression(newExpression);
                    break;
                }
                case IdentifierNameSyntax m:
                {
                    var newExpression = m.WithIdentifier(Identifier(m.Identifier.Text.ToString() + "Async"));
                    newCallSite = newCallSite.WithExpression(newExpression);
                    break;
                }
                case MemberAccessExpressionSyntax m:
                {
                    var newExpression = m.WithName(IdentifierName(m.Name.ToString() + "Async"));
                    newCallSite = newCallSite.WithExpression(newExpression);

                    // var sm = methodModel.GetSemanticModel(callSite.SyntaxTree.GetRoot().SyntaxTree);
                    // var originalMember = ((MemberAccessExpressionSyntax) callSite.Expression).Expression;
                    // var symbol = sm.GetSymbolInfo(originalMember).Symbol;
                    //
                    // if (symbol != null)
                    // {
                    //     if (symbol is IPropertySymbol)
                    //     {
                    //         var prop = (IPropertySymbol) symbol;
                    //         if (prop.Type.Name == "IScriptOutput")
                    //         {
                    //             newCallSite = newCallSite.WithArgumentList(newCallSite.ArgumentList.AddArguments(Argument(IdentifierName("CancellationToken").WithLeadingTrivia(Space))));
                    //         }
                    //     }
                    //     else if (symbol is IParameterSymbol)
                    //     {
                    //         var param = (IParameterSymbol) symbol;
                    //         if (param.Type.Name == "IScriptOutput")
                    //         {
                    //             addCancellationToken = true;
                    //             newCallSite = newCallSite.WithArgumentList(newCallSite.ArgumentList.AddArguments(Argument(IdentifierName("cancellationToken").WithLeadingTrivia(Space))));
                    //         }
                    //     }
                    // }

                    //if (.ReceiverType.Name == "IScriptOutput" || methodModel.ReceiverType.Name == "ScriptOutputExtensions")
                    //{
                    //    var existingArguments = newCallSite.ArgumentList;
                    //    existingArguments = existingArguments.AddArguments(Argument(IdentifierName("CancellationToken")));
                    //    newCallSite = newCallSite.WithArgumentList(existingArguments);
                    //}

                    break;
                }
                default:
                    throw new NotSupportedException($"Expression not supported: {newCallSite.Expression}");
            }

            var awaitCall = AwaitExpression(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, newCallSite, IdentifierName("ConfigureAwait")))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList<ArgumentSyntax>(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression))))))
                .WithAwaitKeyword(Token(leadingTrivia, SyntaxKind.AwaitKeyword, TriviaList(Space)));
            return awaitCall;
        }

        private async Task<Solution> MakeUppercaseAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        {
            // Compute new uppercase name.
            var identifierToken = typeDecl.Identifier;
            var newName = identifierToken.Text.ToUpperInvariant();

            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            // Produce a new solution that has all references to that type renamed, including the declaration.
            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

            // Return the new solution with the now-uppercase type name.
            return newSolution;
        }
    }

    internal class GraphResults
    {
        public Dictionary<IMethodSymbol, SymbolCallerInfo>MethodGraph { get; } =  new Dictionary<IMethodSymbol, SymbolCallerInfo>();
    }
}
