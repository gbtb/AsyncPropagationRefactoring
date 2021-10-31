using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AsyncPropagation
{
    public class ToAsyncInvocationCodefix
    {
        public async Task<Solution> ExecuteAsync(Solution solution, IEnumerable<SymbolCallerInfo> methodsToRewrite)
        {
            //group types by doc because multiple methods can be declared in same file, and we need to do all changes in one pass
            var solution1 = solution;
            var groupByDoc = methodsToRewrite
                .Select(p =>
                {
                    var callingSyntax = p.CallingSymbol.DeclaringSyntaxReferences[0].GetSyntax();
                    return (Location: callingSyntax.GetLocation(), SymbolCallerInfo: p);
                }).GroupBy(t => solution1.GetDocument(t.Location.SourceTree));

            foreach (var group in groupByDoc)
            {
                var oldSolutionDoc = group.Key;
                if (oldSolutionDoc == null)
                {
                    //_logger.LogError("Failed to find docs for some symbols location: {Symbols}", "TODO");
                    continue;
                }
                
                var oldDocRoot = await oldSolutionDoc.GetSyntaxRootAsync();
                if (oldDocRoot == null)
                {
                    //_logger.LogError("Failed to get syntax root of document: {Doc}", oldSolutionDoc.Name);
                    continue;
                }

                var rewriter = new ToAsyncMethodRewriter(oldDocRoot, group);
                var newSolutionDoc = solution.GetDocument(oldSolutionDoc.Id);
                var newSolutionRoot = await newSolutionDoc.GetSyntaxRootAsync();
                
                var newRoot = rewriter.Visit(newSolutionRoot);

                solution = newSolutionDoc.WithSyntaxRoot(newRoot).Project.Solution;
            }

            return solution;
        }
    }
}