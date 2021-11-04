using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public class CallLocation: ILocation
    {
        public CallLocation(Location location)
        {
            Location = location;
        }

        public Location Location { get; }
    }
}