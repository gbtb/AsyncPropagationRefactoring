using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public class MethodSignatureLocation: ILocation
    {
        public MethodSignatureLocation(Location location)
        {
            Location = location;
        }

        public Location Location { get; }
    }
}