using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncPropagation.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation.Shared
{
    public class MethodCallFactory
    {
        internal static async Task<MethodCall> CreateMethodCallAsync(Solution solution, IEnumerable<MethodDeclarationSyntax> methodDeclarations, Location location)
        {
            var doc = solution.GetDocument(location.SourceTree);
            if (doc == null)
                return MethodCall.NullObject;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null)
                return MethodCall.NullObject;
                    
            var invocation = methodDeclarations.Where(decl => decl.FullSpan.Contains(location.SourceSpan))
                .Select(decl => (decl.FindNode(location.SourceSpan)
                        .AncestorsAndSelf()
                        .OfType<InvocationExpressionSyntax>().First(), decl)
                ).FirstOrDefault();
            
            if (invocation.Item1 == null)
                return MethodCall.NullObject;

            return new MethodCall(doc, invocation.Item1, invocation.decl);
        }
    }
}