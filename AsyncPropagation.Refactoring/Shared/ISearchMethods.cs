using System.Collections.Generic;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation.Shared
{
    internal interface ISearchMethods
    {
        bool ShouldSearchForCallers(IMethodSymbol callingMethodSymbol,
            IEnumerable<MethodDeclarationSyntax> methodDeclarationSyntaxes);

        Task<MethodCall> CreateMethodCallAsync(Solution solution, ISymbol referencerCallingSymbol,
            IEnumerable<MethodDeclarationSyntax> methodDeclarations,
            Location location);
    }
}