using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using AsyncPropagation.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AsyncPropagation.ToSync
{
    internal class ToSyncInvocationConverter
    {
        internal async Task<Solution> ExecuteAsync(Solution solution, IMethodSymbol startMethod,
            CancellationToken token)
        {
            var invocationChainFinder = new InvocationChainFinder(new ToSyncSearchMethods());
            var nodeToChange = await invocationChainFinder.GetMethodCallsAsync(solution, startMethod, token);
            
            //group types by doc because multiple methods can be declared in same file, and we need to do all changes in one pass
            var groupByDoc = nodeToChange
                .GroupBy(l => l.Doc);

            foreach (var group in groupByDoc)
            {
                var doc = group.Key;

                var root = await group.First().Node.SyntaxTree.GetRootAsync(token);
                
                
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

                        var nodeToReplace = trackedCall.Ancestors().OfType<AwaitExpressionSyntax>().FirstOrDefault() ??
                                           (SyntaxNode)trackedCall;

                        var newCall = CreateNewCall(trackedCall, nodeToReplace.GetLeadingTrivia());
                        newMethodSyntaxTree = newMethodSyntaxTree.ReplaceNode(nodeToReplace, newCall);
                    }

                    var decl = (methodDeclarationLoc as MethodSignature)!;
                    if (!decl.KeepUntouched)
                        newMethodSyntaxTree = RewriteMethodSignature(newMethodSyntaxTree, decl.IsInterfaceMember);
                    root = root.ReplaceNode(oldMethodSyntaxTree, newMethodSyntaxTree);
                }

                solution = solution.WithDocumentSyntaxRoot(doc.Id, root);
            }

            return solution;
        }

        private MethodDeclarationSyntax RewriteMethodSignature(MethodDeclarationSyntax methodDeclaration, bool isAbstractDeclaration)
        {
            TypeSyntax asyncReturnType;
            SyntaxTokenList methodModifiers;

            var modifiersStr = methodDeclaration.Modifiers.ToString(); //it's surprisingly hard to compare modifiers
            if (modifiersStr.Contains("async"))
            {
                var asyncMod = methodDeclaration.Modifiers.First(m => m.IsKind(SyntaxKind.AsyncKeyword));
                methodModifiers = methodDeclaration.Modifiers.Remove(asyncMod);
            }
            else
            {
                methodModifiers = methodDeclaration.Modifiers;
            }
            
            if (methodDeclaration.ReturnType is IdentifierNameSyntax identifierNameSyntax && identifierNameSyntax.ToString() == "Task")
            {
                var trailingTrivia = methodDeclaration.ReturnType.GetTrailingTrivia();
                asyncReturnType = PredefinedType(Token(SyntaxKind.VoidKeyword)).WithTrailingTrivia(trailingTrivia);
            }else if (methodDeclaration.ReturnType is GenericNameSyntax genericNameSyntax && genericNameSyntax.Identifier.ToString() == "Task")
            {
                var trailingTrivia = methodDeclaration.ReturnType.GetTrailingTrivia();
                asyncReturnType = genericNameSyntax.TypeArgumentList.Arguments.First().WithTrailingTrivia(trailingTrivia);
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
            if (methodDeclaration.Identifier.Text.EndsWith("Async"))
                return Identifier(RemoveAsyncSuffix(methodDeclaration.Identifier.Text));
            else
                return methodDeclaration.Identifier;
        }
        
        
        
        private SyntaxNode CreateNewCall(InvocationExpressionSyntax newCallSite, SyntaxTriviaList leadingTrivia)
        {
            ExpressionSyntax newExpression = newCallSite.Expression switch
            {
                MemberBindingExpressionSyntax node => node.Name.ToString().EndsWith("Async")
                    ? node.WithName(IdentifierName(RemoveAsyncSuffix(node.Name.ToString())))
                    : node,
                IdentifierNameSyntax node => node.Identifier.Text.EndsWith("Async")
                    ? node.WithIdentifier(Identifier(RemoveAsyncSuffix(node.Identifier.Text)))
                    : node,
                MemberAccessExpressionSyntax node => node.Name.ToString().EndsWith("Async") ? 
                    node.WithName(IdentifierName(RemoveAsyncSuffix(node.Name.ToString())))
                    : node,
                _ => throw new ArgumentOutOfRangeException()
            };
            return newCallSite.WithExpression(newExpression.WithoutTrivia())
                .WithLeadingTrivia(leadingTrivia);
        }

        private string RemoveAsyncSuffix(string toString)
        {
            return toString.Substring(0, toString.Length - 5);
        }
    }
}