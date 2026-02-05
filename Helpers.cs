using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Synapse.Crypto.Bfx
{
    public static class Helpers
    {

        /// <summary>
        /// Calculates the minimum price increment (tick size) for a given price.
        /// </summary>
        /// <param name="price">The price for which to calculate the tick size.</param>
        /// <param name="precision">The number of decimal places to use when determining the tick size. Must be at least 1. Defaults to 5 if not
        /// specified.</param>
        /// <returns>The calculated tick size as a double, representing the smallest allowable price increment for the specified
        /// price and precision.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="price"/> is less than or equal to zero, or if <paramref name="precision"/> is less
        /// than 1.</exception>
        public static double GetTickSize(this double price, int precision = 5)
        {
            //tickSize = 10 ^ (floor(log10(P)) - precision + 1)
            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");
            if (precision < 1)
                throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be at least 1.");
            var log10 = Math.Log10((double)price);
            var floorLog10 = Math.Floor(log10);
            var tickSize = Math.Pow(10, floorLog10 - precision + 1);
            return tickSize;
        }


    }
}
