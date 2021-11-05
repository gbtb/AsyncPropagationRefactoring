using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation
{
    internal class MethodCall : INodeToChange<InvocationExpressionSyntax>, IEquatable<MethodCall>
    {
        internal MethodCall(Document doc, InvocationExpressionSyntax node, MethodDeclarationSyntax containingMethod)
        {
            Doc = doc;
            Node = node;
            ContainingMethod = containingMethod;
        }

        public InvocationExpressionSyntax Node { get; }

        public MethodDeclarationSyntax ContainingMethod { get; }

        public Document Doc { get; }
        
        public static MethodCall NullObject => new MethodCall(null!, null!, null!);
        
        public bool Equals(MethodCall? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Node.Equals(other.Node);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MethodCall)obj);
        }

        public override int GetHashCode()
        {
            return Node.GetHashCode();
        }

        public static bool operator ==(MethodCall? left, MethodCall? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(MethodCall? left, MethodCall? right)
        {
            return !Equals(left, right);
        }
    }
}