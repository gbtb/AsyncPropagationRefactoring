using System.Collections.Generic;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using AsyncPropagation.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation.ToAsync
{
    public class ToAsyncSearchMethods: ISearchMethods
    {
        public bool ShouldSearchForCallers(IMethodSymbol callingMethodSymbol,
            IEnumerable<MethodDeclarationSyntax> methodDeclarationSyntaxes)
        {
            return !callingMethodSymbol.IsAsync &&
                   callingMethodSymbol.ReturnType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks";
        }

        public async Task<MethodCall> CreateMethodCallAsync(Solution solution, ISymbol referencerCallingSymbol, IEnumerable<MethodDeclarationSyntax> methodDeclarations,
            Location location)
        {
            return await MethodCallFactory.CreateMethodCallAsync(solution, methodDeclarations, location);
        }
    }
}