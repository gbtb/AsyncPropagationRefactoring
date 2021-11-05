using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public interface INodeToChange<out TNode> where TNode: SyntaxNode
    {
        Document Doc { get; }
        
        TNode Node { get; }
    }
}