using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace AsyncPropagation
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(AsyncPropagationCodeFixProvider))]
    public class AsyncPropagationCodeFixProvider : CodeRefactoringProvider
    {
        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var solution = context.Document.Project.Solution;
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var declaration = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            if (declaration == null)
                return;
            
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            var methodSymbol = semanticModel.GetDeclaredSymbol(declaration);
            if (methodSymbol == null)
                return;
            
            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, context.CancellationToken);
            
            if (callers?.Any() != true)
                return;

            if (!methodSymbol.IsAsync)
            {
                var codeFix = new ToAsyncInvocationConverter();
                try
                {
                    context.RegisterRefactoring(
                        CodeAction.Create(
                            title: CodeFixResources.ToAsyncCodeFixTitle,
                            createChangedSolution: c => codeFix.ExecuteAsync(solution, methodSymbol, context.CancellationToken),
                            equivalenceKey: methodSymbol.ToDisplayString()));
                }
                catch (TaskCanceledException)
                {
                }
            }
            else
            {
                var codeFix = new ToSyncInvocationConverter();
                try
                {
                    context.RegisterRefactoring(
                        CodeAction.Create(
                            title: CodeFixResources.FromAsyncCodeFixTitle,
                            createChangedSolution: c => codeFix.ExecuteAsync(solution, methodSymbol, context.CancellationToken),
                            equivalenceKey: methodSymbol.ToDisplayString()));
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }
}
