using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation
{
    [DebuggerDisplay("{MethodSymbol} CallSites: {CallSites.Count}")]
    class MethodCallToTransform
    {
        public MethodCallToTransform(IMethodSymbol methodSymbol, MethodDeclarationSyntax callerMethod)
        {
            MethodSymbol = methodSymbol;
            CallerMethod = callerMethod;
            CallSites = new List<InvocationExpressionSyntax>();
        }

        public IMethodSymbol MethodSymbol { get; }

        public MethodDeclarationSyntax CallerMethod { get; }

        public MethodDeclarationSyntax AsyncCalleeMethod { get; set; }

        public List<InvocationExpressionSyntax> CallSites { get; }
    }
}