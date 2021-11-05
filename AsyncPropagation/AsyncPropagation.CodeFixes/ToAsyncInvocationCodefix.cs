using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AsyncPropagation
{
    public class ToAsyncInvocationCodefix
    {
        private readonly bool _useConfigureAwait;
        private readonly bool _ensureAsyncPostfix = true;

        public async Task<Solution> ExecuteAsync(Solution solution, IMethodSymbol startMethod,
            List<ILocation> locations)
        {
            //group types by doc because multiple methods can be declared in same file, and we need to do all changes in one pass
            var solution1 = solution;
            var groupByDoc = locations
                .GroupBy(l => solution1.GetDocument(l.Location.SourceTree));

            var startMethodSyntaxNode = startMethod.DeclaringSyntaxReferences[0].GetSyntax();
            var startMethodDoc = solution1.GetDocument(startMethodSyntaxNode.SyntaxTree);

            foreach (var group in groupByDoc)
            {
                var doc = group.Key;
                var tree = await doc.GetSyntaxTreeAsync();
                var root = tree.GetRoot();

                List<(SyntaxNode OldNode, SyntaxNode NewNode)> replacePairs =
                    new List<(SyntaxNode oldNode, SyntaxNode newNode)>();

                var lookup = group.ToLookup(l => l is MethodSignatureLocation);
                
                foreach (var methodDeclarationLoc  in lookup[true])
                {
                    var oldMethodSyntaxTree = root.FindNode(methodDeclarationLoc.Location.SourceSpan)
                        .AncestorsAndSelf()
                        .OfType<MethodDeclarationSyntax>().First();

                    var callsToRewrite = lookup[false]
                        .Where(n => oldMethodSyntaxTree.FullSpan.Contains(n.Location.SourceSpan))
                        .Select(n => oldMethodSyntaxTree.FindNode(n.Location.SourceSpan)
                            .AncestorsAndSelf()
                            .OfType<InvocationExpressionSyntax>().First()
                        );

                    var newMethodSyntaxTree = oldMethodSyntaxTree;
                    foreach (var call in callsToRewrite)
                    {
                        var awaitCall = _useConfigureAwait ? InvocationWithConfigureAwait(call, call.GetLeadingTrivia()) 
                            : InvocationWithAwait(call, call.GetLeadingTrivia());
                        newMethodSyntaxTree = newMethodSyntaxTree.ReplaceNode(call, awaitCall);
                    }
                    
                    newMethodSyntaxTree = RewriteMethodSignature(newMethodSyntaxTree);
                    root = root.ReplaceNode(oldMethodSyntaxTree, newMethodSyntaxTree);
                }
                
                solution = solution.WithDocumentSyntaxRoot(doc.Id, root);
                
                // foreach (var location in group)
                // {
                //     
                // }
                // var oldSolutionDoc = group.Key;
                // if (oldSolutionDoc == null)
                // {
                //     //_logger.LogError("Failed to find docs for some symbols location: {Symbols}", "TODO");
                //     continue;
                // }
                //
                // var oldDocRoot = await oldSolutionDoc.GetSyntaxRootAsync();
                // if (oldDocRoot == null)
                // {
                //     //_logger.LogError("Failed to get syntax root of document: {Doc}", oldSolutionDoc.Name);
                //     continue;
                // }
                //
                // var rewriter = new ToAsyncMethodRewriter(oldDocRoot, group, startMethodDoc?.Id == oldSolutionDoc.Id ? startMethodSyntaxNode : null);
                // var newSolutionDoc = solution.GetDocument(oldSolutionDoc.Id);
                // var newSolutionRoot = await newSolutionDoc.GetSyntaxRootAsync();
                //
                // var newRoot = rewriter.Visit(newSolutionRoot);
                //
                // solution = newSolutionDoc.WithSyntaxRoot(newRoot).Project.Solution;
            }

            return solution;
        }
        
        private MethodDeclarationSyntax RewriteMethodSignature(MethodDeclarationSyntax methodDeclaration)
        {
            TypeSyntax asyncReturnType;
            SyntaxTokenList methodModifiers;

            if (methodDeclaration.Modifiers.ToString().Contains("async"))
                methodModifiers = methodDeclaration.Modifiers;
            else
                methodModifiers = methodDeclaration.Modifiers.Add(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space));
            
            if (methodDeclaration.ReturnType is PredefinedTypeSyntax voidType && voidType.Keyword.Kind() == SyntaxKind.VoidKeyword)
            {
                asyncReturnType = IdentifierName("Task").WithTrailingTrivia(Space);
            }
            else if ((methodDeclaration.ReturnType is IdentifierNameSyntax identifierNameSyntax && identifierNameSyntax.ToString() != "Task")
                     || (methodDeclaration.ReturnType is GenericNameSyntax genericNameSyntax && genericNameSyntax.Identifier.ToString() != "Task"))
            {
                var trailingTrivia = methodDeclaration.ReturnType.GetTrailingTrivia();

                asyncReturnType = GenericName(
                        Identifier("Task"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SingletonSeparatedList(methodDeclaration.ReturnType.WithoutTrailingTrivia()))).WithTrailingTrivia(trailingTrivia);
            }
            else
            {
                asyncReturnType = methodDeclaration.ReturnType;
            }

            methodDeclaration = methodDeclaration.WithReturnType(asyncReturnType)
                .WithIdentifier(GetMethodName(methodDeclaration))
                .WithModifiers(methodModifiers);

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
            return AwaitExpression(newCallSite.WithExpression(newExpression))
                .WithAwaitKeyword(Token(TriviaList(Space), SyntaxKind.AwaitKeyword, TriviaList(Space)))
                .WithLeadingTrivia(leadingTrivia);
        }
    }
}