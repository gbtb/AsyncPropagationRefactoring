using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace AsyncPropagation
{
    //[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncPropagationCodeFixProvider)), Shared]
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(AsyncPropagationCodeFixProvider))]
    public class AsyncPropagationCodeFixProvider : CodeRefactoringProvider
    {
        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var solution = context.Document.Project.Solution;
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;
            
            var declaration = root.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            if (declaration == null)
                return;
            
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            var methodSymbol = semanticModel.GetDeclaredSymbol(declaration);
            if (methodSymbol == null)
                return;
            
            if (methodSymbol.IsAsync)
                return;

            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, context.CancellationToken);
            
            if (callers?.Any() != true)
                return;
            
            var codeFix = new ToAsyncInvocationConverter();
            try
            {
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: CodeFixResources.CodeFixTitle,
                        createChangedSolution: c => codeFix.ExecuteAsync(solution, methodSymbol, context.CancellationToken),
                        equivalenceKey: nameof(CodeFixResources.CodeFixTitle)));
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
