using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using AsyncPropagation.Shared;
using AsyncPropagation.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation.ToSync
{
    public class ToSyncSearchMethods: ISearchMethods
    {
        private readonly Dictionary<ISymbol, int> _awaitCounts =
            new Dictionary<ISymbol, int>((SymbolEqualityComparer.Default));
        
        public bool ShouldSearchForCallers(IMethodSymbol callingMethodSymbol,
            IEnumerable<MethodDeclarationSyntax> methodDeclarationSyntaxes)
        {
            if (!callingMethodSymbol.IsAsync &&
                callingMethodSymbol.ReturnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
                return false;

            if (_awaitCounts.TryGetValue(callingMethodSymbol, out var awaitCount))
            {
                var totalAwaitCount = methodDeclarationSyntaxes.SelectMany(s => s.DescendantNodes())
                    .OfType<AwaitExpressionSyntax>().Count();
                if (totalAwaitCount > awaitCount)
                    return false; //Heuristic: this method has other async calls, so it can't be made synchronous
            }

            return true;
        }

        public async Task<MethodCall> CreateMethodCallAsync(Solution solution, ISymbol referencerCallingSymbol,
            IEnumerable<MethodDeclarationSyntax> methodDeclarations, Location location)
        {
            var call = await MethodCallFactory.CreateMethodCallAsync(solution, methodDeclarations, location);
            if (call == MethodCall.NullObject)
                return MethodCall.NullObject;

            if (call.Node.Ancestors().OfType<AwaitExpressionSyntax>().Any())
            {
                _awaitCounts.AddOrUpdate(referencerCallingSymbol, x => ++x, 1);
            }

            return call;
        }
    }
}