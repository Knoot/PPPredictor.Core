using System;

namespace PPPredictor.Core
{
    internal static class ImproveTargetSearch
    {
        internal static double? FindRawPP(Func<double, double> weightedGain, double target, double maxPP)
        {
            if (!Finite(target) || target <= 0 || !Finite(maxPP) || maxPP <= 0) return null;
            double maximumGain = weightedGain(maxPP);
            if (!Finite(maximumGain) || maximumGain < target) return null;
            double low = 0, high = maxPP;
            for (int i = 0; i < 48; i++)
            {
                double mid = (low + high) / 2;
                double gain = weightedGain(mid);
                if (!Finite(gain)) return null;
                if (gain >= target) high = mid;
                else low = mid;
            }
            return high;
        }

        internal static double? FindDisplayedPercentage(Func<double, double> calculatePP, double targetRawPP)
        {
            if (!Finite(targetRawPP) || targetRawPP < 0) return null;
            bool Meets(double percentage)
            {
                double pp = calculatePP(percentage);
                return Finite(pp) && pp >= targetRawPP;
            }
            if (Meets(0)) return 0;
            double previous = 0;
            // Keep the interval scan: some leaderboard contexts are not monotone over 0..100.
            for (int i = 1; i <= 1000; i++)
            {
                double percentage = i / 10d;
                if (Meets(percentage))
                {
                    double low = previous, high = percentage;
                    for (int step = 0; step < 32; step++)
                    {
                        double mid = (low + high) / 2;
                        if (Meets(mid)) high = mid;
                        else low = mid;
                    }
                    // Validate the displayed hundredth, not a rounded-down continuous solution.
                    for (int tick = (int)Math.Floor(low * 100) + 1; tick <= i * 10; tick++)
                        if (Meets(tick / 100d)) return tick / 100d;
                }
                previous = percentage;
            }
            return null;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
