using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public class ToSyncSearchMethods: ISearchMethods
    {
        public bool ShouldSearchForCallers(IMethodSymbol callingMethodSymbol)
        {
            return callingMethodSymbol.IsAsync ||
                   callingMethodSymbol.ReturnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
        }
    }
}