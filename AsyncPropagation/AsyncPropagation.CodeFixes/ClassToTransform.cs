using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation
{
    [DebuggerDisplay("{TypeSymbol}")]
    class ClassToTransform
    {
        public ClassToTransform(ITypeSymbol typeSymbol)
        {
            TypeSymbol = typeSymbol;
            MethodCalls = new List<MethodCallToTransform>();
            Namespace = GetNamespace(typeSymbol);
        }

        public ITypeSymbol TypeSymbol { get; }

        public string Namespace { get; }

        public TypeDeclarationSyntax AsyncDeclarationTypeSyntax { get; set; }

        public List<MethodCallToTransform> MethodCalls { get; }
        
        public static string GetNamespace(ISymbol symbol)
        {
            if (string.IsNullOrEmpty(symbol.ContainingNamespace?.Name))
            {
                return null;
            }

            var restOfResult = GetNamespace(symbol.ContainingNamespace);
            var result = symbol.ContainingNamespace.Name;

            if (restOfResult != null)
                result = restOfResult + '.' + result;

            return result;
        }
    }
    
    
}