using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AsyncPropagation
{
    public class MethodLocation: ILocation
    {
        public MethodLocation(Location location)
        {
            Location = location;
        }

        public Location Location { get; }
        
        public List<CallLocation> CallLocations { get; }
    }
}