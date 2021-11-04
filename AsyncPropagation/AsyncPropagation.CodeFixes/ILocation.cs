using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public interface ILocation
    {
        Location Location { get; }
    }
}