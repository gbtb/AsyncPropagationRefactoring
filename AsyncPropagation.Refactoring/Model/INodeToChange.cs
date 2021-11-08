using Microsoft.CodeAnalysis;

namespace AsyncPropagation.Model
{
    public interface INodeToChange<out TNode> where TNode: SyntaxNode
    {
        Document Doc { get; }
        
        TNode Node { get; }
    }
}