using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AsyncPropagation
{
    public class ToAsyncMethodRewriter: CSharpSyntaxRewriter
    {
        private bool _doRewrite;
        private readonly bool _useConfigureAwait = false;
        private readonly bool _ensureAsyncPostfix = true;
        private readonly List<SyntaxNode> _invocationsToRewrite;
        private readonly List<SyntaxNode> _methodDeclarationsToRewrite;

        public ToAsyncMethodRewriter(SyntaxNode oldDocRoot, IEnumerable<(Location Location, SymbolCallerInfo SymbolCallerInfo)> methodsToRewrite)
        {
            _invocationsToRewrite = new List<SyntaxNode>();
            _methodDeclarationsToRewrite = new List<SyntaxNode>();
            foreach (var method in methodsToRewrite)
            {
                _methodDeclarationsToRewrite.Add(oldDocRoot.FindNode(method.Location.SourceSpan));
                _invocationsToRewrite.AddRange(method.SymbolCallerInfo.Locations.Select(inv => oldDocRoot.FindNode(inv.SourceSpan).Parent));
            }
        }

        private bool CheckNodeIsInList(List<SyntaxNode> syntaxNodes, SyntaxNode node)
        {
            return syntaxNodes.Find(n => n.IsEquivalentTo(node)) != null;
        }
        
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (!CheckNodeIsInList(_methodDeclarationsToRewrite, node))
                return base.VisitMethodDeclaration(node);

            if (!node.Modifiers.Any(SyntaxKind.AsyncKeyword))
                node = RewriteMethodSignature(node);

            return base.VisitMethodDeclaration(node);
        }

        private MethodDeclarationSyntax RewriteMethodSignature(MethodDeclarationSyntax methodDeclaration)
        {
            TypeSyntax asyncReturnType;
            if (methodDeclaration.ReturnType is PredefinedTypeSyntax voidType && voidType.Keyword.Kind() == SyntaxKind.VoidKeyword)
            {
                asyncReturnType = IdentifierName("Task").WithTrailingTrivia(Space);
            }
            else
            {
                var trailingTrivia = methodDeclaration.ReturnType.GetTrailingTrivia();

                asyncReturnType = GenericName(
                        Identifier("Task"))
                    .WithTypeArgumentList(
                        TypeArgumentList(
                            SingletonSeparatedList(methodDeclaration.ReturnType.WithoutTrailingTrivia()))).WithTrailingTrivia(trailingTrivia);
            }

            methodDeclaration = methodDeclaration.WithReturnType(asyncReturnType)
                .WithIdentifier(GetMethodName(methodDeclaration))
                .WithModifiers(methodDeclaration.Modifiers.Add(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space)));

            return methodDeclaration;
        }

        private SyntaxToken GetMethodName(MethodDeclarationSyntax methodDeclaration)
        {
            if (_ensureAsyncPostfix && !methodDeclaration.Identifier.Text.EndsWith("Async"))
                return Identifier(methodDeclaration.Identifier.Text + "Async");
            else
                return methodDeclaration.Identifier;
        }

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (!CheckNodeIsInList(_invocationsToRewrite, node))
                return base.VisitInvocationExpression(node);
            
            var leadingTrivia = node.GetLeadingTrivia();

            _doRewrite = true;
            var newExpression = this.Visit(node.Expression);
            _doRewrite = false;
            var newCallSite = node.WithLeadingTrivia(Space).WithExpression((ExpressionSyntax)newExpression);
            
            //todo: support passing tasks without awaiting, if containing method already does so
            var awaitCall = _useConfigureAwait ? InvocationWithConfigureAwait(newCallSite, leadingTrivia) : InvocationWithAwait(newCallSite, leadingTrivia);

            return awaitCall;
        }

        private SyntaxNode InvocationWithAwait(InvocationExpressionSyntax newCallSite, SyntaxTriviaList leadingTrivia)
        {
            return AwaitExpression(newCallSite)
                .WithAwaitKeyword(Token(leadingTrivia, SyntaxKind.AwaitKeyword, TriviaList(Space)));
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

        public override SyntaxNode VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
        {
            if (!_doRewrite || !_ensureAsyncPostfix || node.Name.ToString().EndsWith("Async"))
                return node;
            
            return node.WithName(IdentifierName(node.Name + "Async"));
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!_doRewrite || !_ensureAsyncPostfix || node.Identifier.Text.EndsWith("Async"))
                return node;
            
            return node.WithIdentifier(Identifier(node.Identifier.Text + "Async"));
        }

        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!_doRewrite || !_ensureAsyncPostfix || node.Name.ToString().EndsWith("Async"))
                return node;
            
            return node.WithName(IdentifierName(node.Name + "Async"));
        }
    }
}