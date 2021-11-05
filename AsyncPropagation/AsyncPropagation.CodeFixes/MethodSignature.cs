﻿using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AsyncPropagation
{
    internal class MethodSignature: INodeToChange<MethodDeclarationSyntax>, IEquatable<MethodSignature>
    {
        internal MethodSignature(Document doc, MethodDeclarationSyntax node)
        {
            Doc = doc;
            Node = node;
        }

        public MethodDeclarationSyntax Node { get; }
        
        public Document Doc { get; }

        public static MethodSignature NullObject = new MethodSignature(null!, null!);
        
        public bool Equals(MethodSignature? other)
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
            return Equals((MethodSignature)obj);
        }

        public override int GetHashCode()
        {
            return Node.GetHashCode();
        }

        public static bool operator ==(MethodSignature? left, MethodSignature? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(MethodSignature? left, MethodSignature? right)
        {
            return !Equals(left, right);
        }
    }
}