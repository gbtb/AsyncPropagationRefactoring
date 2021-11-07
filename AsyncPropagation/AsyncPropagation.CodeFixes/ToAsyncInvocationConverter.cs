using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AsyncPropagation
{
    internal class ToAsyncInvocationConverter
    {
        private readonly bool _useConfigureAwait = false;
        private readonly bool _ensureAsyncPostfix = true;

        internal async Task<Solution> ExecuteAsync(Solution solution, IMethodSymbol startMethod,
            CancellationToken token)
        {
            var invocationChainFinder = new InvocationChainFinder(new ToAsyncSearchMethods());
            var nodeToChange = await invocationChainFinder.GetMethodCallsAsync(solution, startMethod, token);
            
            //group types by doc because multiple methods can be declared in same file, and we need to do all changes in one pass
            var groupByDoc = nodeToChange
                .GroupBy(l => l.Doc);

            foreach (var group in groupByDoc)
            {
                var doc = group.Key;

                var root = await group.First().Node.SyntaxTree.GetRootAsync(token);
                var hasUsing = root.DescendantNodesAndSelf().OfType<CompilationUnitSyntax>()
                    .FirstOrDefault()?.Usings.Any(u => u.Name.ToString() == "System.Threading.Tasks");
                
                
                List<(SyntaxNode OldNode, SyntaxNode NewNode)> replacePairs =
                    new List<(SyntaxNode oldNode, SyntaxNode newNode)>();

                var lookup = group.ToLookup(l => l is MethodSignature);

                //we will use node tracking mechanism to perform multiple changes on a doc's SyntaxTree
                //it will allow us to mutate root children and don't lose positions of nodes we haven't yet rewrite
                root = root.TrackNodes(group.Select(n => n.Node));
                foreach (var methodDeclarationLoc  in lookup[true])
                {
                    var oldMethodSyntaxTree =
                        root.GetCurrentNode((methodDeclarationLoc as MethodSignature)!.Node);
                    if (oldMethodSyntaxTree == null)
                        continue;

                    var callsToRewrite = lookup[false]
                        .OfType<MethodCall>()
                        .Where(call => root.GetCurrentNode(call.ContainingMethod) == oldMethodSyntaxTree)
                        .Select(call => call.Node)
                        .ToList();

                    var newMethodSyntaxTree = oldMethodSyntaxTree;
                    foreach (var call in callsToRewrite)
                    {
                        var trackedCall = newMethodSyntaxTree.GetCurrentNode(call);
                        if (trackedCall == null)
                            continue;
                        
                        var awaitCall = _useConfigureAwait ? InvocationWithConfigureAwait(trackedCall, trackedCall.GetLeadingTrivia()) 
                            : InvocationWithAwait(trackedCall, trackedCall.GetLeadingTrivia());
                        newMethodSyntaxTree = newMethodSyntaxTree.ReplaceNode(trackedCall, awaitCall);
                    }
                    
                    newMethodSyntaxTree = RewriteMethodSignature(newMethodSyntaxTree, (methodDeclarationLoc as MethodSignature)!.IsInterfaceMember);
                    root = root.ReplaceNode(oldMethodSyntaxTree, newMethodSyntaxTree);
                }

                if (hasUsing != true)
                    root = AddUsingForTask(root);
                
                solution = solution.WithDocumentSyntaxRoot(doc.Id, root);
            }

            return solution;
        }

        private SyntaxNode AddUsingForTask(SyntaxNode root)
        {
            var compilationUnitSyntax = root.DescendantNodesAndSelf().OfType<CompilationUnitSyntax>().FirstOrDefault();
            if (compilationUnitSyntax == null)
                return root;

            var usings = compilationUnitSyntax.Usings.Add(
                UsingDirective(
                    QualifiedName(
                        QualifiedName(
                            IdentifierName("System"),
                            IdentifierName("Threading")),
                        IdentifierName("Tasks")))
                .WithUsingKeyword(
                    Token(
                        TriviaList(),
                        SyntaxKind.UsingKeyword,
                        TriviaList(
                            Space))));

            return root.ReplaceNode(compilationUnitSyntax, compilationUnitSyntax.WithUsings(usings));
        }

        private MethodDeclarationSyntax RewriteMethodSignature(MethodDeclarationSyntax methodDeclaration, bool isAbstractDeclaration)
        {
            TypeSyntax asyncReturnType;
            SyntaxTokenList methodModifiers;

            var modifiersStr = methodDeclaration.Modifiers.ToString(); //it's surprisingly hard to compare modifiers
            if (modifiersStr.Contains("async") || modifiersStr.Contains("abstract") || isAbstractDeclaration)
                methodModifiers = methodDeclaration.Modifiers;
            else
                methodModifiers = methodDeclaration.Modifiers.Add(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space));
            
            if (methodDeclaration.ReturnType is PredefinedTypeSyntax voidType && voidType.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                asyncReturnType = IdentifierName("Task").WithTrailingTrivia(Space);
            }
            else if ((methodDeclaration.ReturnType is IdentifierNameSyntax identifierNameSyntax && identifierNameSyntax.ToString() != "Task")
                     || (methodDeclaration.ReturnType is GenericNameSyntax genericNameSyntax && genericNameSyntax.Identifier.ToString() != "Task")
                     || methodDeclaration.ReturnType is PredefinedTypeSyntax)
            {
                var trailingTrivia = methodDeclaration.ReturnType.GetTrailingTrivia();

                asyncReturnType = GenericName(
                        Identifier("Task"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SingletonSeparatedList(methodDeclaration.ReturnType.WithoutTrivia()))).WithTrailingTrivia(trailingTrivia);
            }
            else
            {
                asyncReturnType = methodDeclaration.ReturnType;
            }

            methodDeclaration = methodDeclaration.WithReturnType(asyncReturnType.WithLeadingTrivia())
                .WithIdentifier(GetMethodName(methodDeclaration))
                .WithModifiers(methodModifiers)
                .WithLeadingTrivia(methodDeclaration.GetLeadingTrivia())
                ;

            return methodDeclaration;
        }
        
        private SyntaxToken GetMethodName(MethodDeclarationSyntax methodDeclaration)
        {
            if (_ensureAsyncPostfix && !methodDeclaration.Identifier.Text.EndsWith("Async"))
                return Identifier(methodDeclaration.Identifier.Text + "Async");
            else
                return methodDeclaration.Identifier;
        }
        
        private SyntaxNode InvocationWithConfigureAwait(ExpressionSyntax newCallSite, SyntaxTriviaList leadingTrivia)
        {
            return AwaitExpression(
                    InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, newCallSite, IdentifierName("ConfigureAwait"))
                        )
                        .WithArgumentList(
                            ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression))))
                        )
                )
                .WithAwaitKeyword(
                    Token(leadingTrivia, SyntaxKind.AwaitKeyword, TriviaList(Space))
                );
        }
        
        private SyntaxNode InvocationWithAwait(InvocationExpressionSyntax newCallSite, SyntaxTriviaList leadingTrivia)
        {
            ExpressionSyntax newExpression = newCallSite.Expression switch
            {
                MemberBindingExpressionSyntax node => !_ensureAsyncPostfix || node.Name.ToString().EndsWith("Async")
                    ? node
                    : node.WithName(IdentifierName(node.Name + "Async")),
                IdentifierNameSyntax node => !_ensureAsyncPostfix || node.Identifier.Text.EndsWith("Async")
                    ? node
                    : node.WithIdentifier(Identifier(node.Identifier.Text + "Async")),
                MemberAccessExpressionSyntax node => !_ensureAsyncPostfix || node.Name.ToString().EndsWith("Async") ? 
                    node 
                    : node.WithName(IdentifierName(node.Name + "Async")),
                _ => throw new ArgumentOutOfRangeException()
            };
            return AwaitExpression(newCallSite.WithExpression(newExpression.WithoutTrivia()))
                .WithAwaitKeyword(Token(TriviaList(Space), SyntaxKind.AwaitKeyword, TriviaList(Space)))
                .WithLeadingTrivia(leadingTrivia);
        }
    }
}