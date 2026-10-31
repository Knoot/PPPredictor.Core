using System;
using System.Collections.Generic;
using System.Linq;

namespace PPPredictor.Core.DataType.Curve
{
    // Port of BeatLeader/beatleader-website src/utils/beatleader/rebalanced-pp.js.
    internal sealed class BeatLeaderV4Curve
    {
        private static readonly (double X, double Y)[] BaseTable = {
            (0, 0), (0.6, 0.307), (0.65, 0.355), (0.7, 0.414), (0.75, 0.485),
            (0.8, 0.568), (0.825, 0.605), (0.85, 0.649), (0.875, 0.699),
            (0.9, 0.753), (0.91, 0.781), (0.92, 0.813), (0.93, 0.867),
            (0.94, 0.931), (0.95, 1), (0.955, 1.039), (0.96, 1.094),
            (0.965, 1.167), (0.97, 1.256), (0.9725, 1.315), (0.975, 1.392),
            (0.9775, 1.49), (0.98, 1.618), (0.9825, 1.786), (0.985, 2.007),
            (0.9875, 2.303), (0.99, 2.7), (0.9925, 3.241), (0.995, 4.01),
            (0.9975, 5.158), (0.999, 6.241), (1, 7.424)
        };
        private static readonly double[] Slopes = BuildSlopes();
        private static readonly double[][] Settings = {
            new[] { 0.955, 1.0, -5.3, 4.86, 0.6, 2.88, 2.46 },
            new[] { 0.96, 0.99, -3.0, 4.3, 0.66, 4.8, 1.85 },
            new[] { 0.965, 0.988, -2.4, 4.5, 0.64, 4.5, 1.95 },
            new[] { 0.97, 0.986, -2.2, 4.7, 0.62, 4.2, 2.05 },
            new[] { 0.975, 0.985, -1.8, 4.86, 0.6, 3.8, 2.15 },
            new[] { 0.98, 0.985, -1.4, 4.86, 0.6, 3.35, 2.3 },
            new[] { 0.985, 0.985, -1.2, 4.86, 0.6, 2.88, 2.46 },
            new[] { 0.99, 0.9915, -1.2, 4.86, 0.6, 2.88, 2.46 },
            new[] { 0.995, 0.9915, -1.2, 4.86, 0.6, 2.88, 2.46 }
        };
        private readonly (double X, double Y)[] points;
        private readonly double stars;

        internal BeatLeaderV4Curve(PPPStarRating rating)
        {
            points = BuildCurve(rating);
            double at96 = PassPP(rating.PassRating) + AccPP(Value(0.96), rating.AccRating) + TechPP(0.96, rating.TechRating);
            stars = StarsWithoutBonus(at96);
            if (at96 > 0 && stars < 8.6454)
            {
                double lo = stars, hi = 8.6454;
                while (hi - lo > 1e-6)
                {
                    double mid = (lo + hi) / 2;
                    if (mid < StarsWithoutBonus(at96 * LowStarMultiplier(mid))) lo = mid;
                    else hi = mid;
                }
                stars = (lo + hi) / 2;
            }
        }

        internal double Calculate(double accuracy, PPPStarRating rating, bool golf)
        {
            if (!(accuracy > 0) || accuracy > 1) return 0;
            double multiplier = rating.Multiplier;
            double value = Value(accuracy), at96 = Value(0.96), power = CurvePower(stars);
            if (power != 1 && value > 0 && at96 > 0) value = at96 * Math.Pow(value / at96, power);
            double acc = golf ? accuracy * rating.AccRating * multiplier * 42 : AccPP(value, rating.AccRating * multiplier);
            double total = 1.023 * (PassPP(rating.PassRating * multiplier) + acc + TechPP(accuracy, rating.TechRating * multiplier)) * LowStarMultiplier(stars);
            return double.IsNaN(total) || double.IsInfinity(total) ? 0 : total;
        }

        private static double PassPP(double rating)
        {
            if (rating > 13) rating = 13 + (rating - 13) * 0.5;
            double value = 12.3 * Math.Exp(0.85966 * Math.Pow(rating, 1 / 2.62)) - 30;
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
        }

        private static double AccPP(double value, double rating) => rating > 0 ? value * 21.584 * Math.Pow(rating, 1.2163) : 0;

        private static double TechPP(double accuracy, double rating)
        {
            if (!(rating > 0)) return 0;
            if (rating > 12) rating = 12 + (rating - 12) * 0.33;
            return Math.Exp(3.5 * accuracy) * 0.1612 * Math.Pow(rating, 1.2477);
        }

        private static double StarsWithoutBonus(double pp) => pp > 0 ? 1.1653 * Math.Pow(pp / 83.6925, 1.17507) : pp / 83.6925;
        private static double LowStarMultiplier(double value) => value >= 8.6454 ? 1 : 1 + 0.0063371 * Math.Pow(8.6454 - Math.Max(value, 0), 1.36);

        private static double CurvePower(double value)
        {
            if (value < 4.803)
                return value <= 2.8818 ? 1.08 : 1.08 - 0.08 * (value - 2.8818) / (4.803 - 2.8818);
            if (value >= 9.606) return 0.9;
            return value <= 6.7242 ? 1 : 1 - 0.1 * (value - 6.7242) / (9.606 - 6.7242);
        }

        private double Value(double accuracy) => Interpolate(points, accuracy);

        private static double Interpolate((double X, double Y)[] table, double accuracy)
        {
            int lo = 0, hi = table.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (table[mid].X < accuracy) lo = mid;
                else hi = mid;
            }
            double t = (accuracy - table[lo].X) / (table[hi].X - table[lo].X);
            return table[lo].Y + t * (table[hi].Y - table[lo].Y);
        }

        private static double[] BuildSlopes()
        {
            int n = BaseTable.Length;
            var h = new double[n - 1];
            var d = new double[n - 1];
            var slopes = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = BaseTable[i + 1].X - BaseTable[i].X;
                d[i] = (BaseTable[i + 1].Y - BaseTable[i].Y) / h[i];
            }
            slopes[0] = d[0];
            slopes[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
            return slopes;
        }

        private static double SmoothValue(double accuracy)
        {
            if (accuracy <= 0) return 0;
            if (accuracy >= 1) return BaseTable[BaseTable.Length - 1].Y;
            int i = 0;
            while (BaseTable[i + 1].X < accuracy) i++;
            double h = BaseTable[i + 1].X - BaseTable[i].X;
            double t = (accuracy - BaseTable[i].X) / h, t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * BaseTable[i].Y + (t3 - 2 * t2 + t) * h * Slopes[i]
                + (-2 * t3 + 3 * t2) * BaseTable[i + 1].Y + (t3 - t2) * h * Slopes[i + 1];
        }

        private static double[] Parameters(double prediction)
        {
            if (prediction <= Settings[0][0]) return Settings[0];
            for (int i = 1; i < Settings.Length; i++)
            {
                if (prediction > Settings[i][0]) continue;
                double t = (prediction - Settings[i - 1][0]) / (Settings[i][0] - Settings[i - 1][0]);
                return Settings[i - 1].Zip(Settings[i], (a, b) => a + t * (b - a)).ToArray();
            }
            return Settings[Settings.Length - 1];
        }

        private sealed class AnalyticCurve
        {
            internal readonly double A;
            private readonly double c, k, w, k2, p2, m;
            internal AnalyticCurve(double a = 1, double c = 1, double k = 4.86, double w = 0.6, double k2 = 2.88, double p2 = 2.46)
            {
                A = a; this.c = c; this.k = k; this.w = w; this.k2 = k2; this.p2 = p2;
                m = (1 - 0.256) / 0.35 * (1 - w + w * k * Math.Exp(k) / (Math.Exp(k) - 1));
            }
            private double F2(double x) => 1 + m * (x - 0.95) + (6.424 - 0.05 * m)
                * (Math.Exp(k2 * Math.Pow((x - 0.95) / 0.05, p2)) - 1) / (Math.Exp(k2) - 1);

            internal double Value(double x)
            {
                if (x <= 0.6) return 0.256 / 0.6 * x;
                if (x <= 0.95)
                {
                    double u = (x - 0.6) / 0.35;
                    return 0.256 + (1 - 0.256) * ((1 - w) * u + w * (Math.Exp(k * u) - 1) / (Math.Exp(k) - 1));
                }
                if (x <= A) return F2(x);
                double uA = (A - 0.95) / 0.05;
                double s = m + (6.424 - 0.05 * m) * Math.Exp(k2 * Math.Pow(uA, p2)) / (Math.Exp(k2) - 1)
                    * k2 * p2 / 0.05 * Math.Pow(uA, p2 - 1);
                double s1 = s * (1 - c);
                if (Math.Abs(A - 1) < 1e-9) return F2(A) + s1 * (x - A);
                return F2(A) + s1 * (x - A) + (s - s1) * (1 - A) * (1 - Math.Exp(-(x - A) / (1 - A)));
            }
        }

        private static double Nerf(double value, double start, double reference, double minimum)
        {
            if (value <= 0 || value >= start) return 1;
            return Math.Min(Math.Max(1 - (1 - minimum) * Math.Log(start / value) / Math.Log(start / reference), minimum), 1);
        }

        private static double Round6(double value) => Math.Floor(value * 1e6 + 0.5) / 1e6;

        private static (double X, double Y)[] BuildCurve(PPPStarRating rating)
        {
            double prediction = rating.PredictedAcc;
            bool dynamic = prediction > 0.6 && prediction < 0.9995;
            double[] row = Parameters(prediction);
            bool modified = dynamic && !(row[1] >= 0.9995 && row[3] == 4.86 && row[4] == 0.6 && row[5] == 2.88 && row[6] == 2.46);
            double tilt = dynamic ? 0.2 * rating.SliderPercentage : 0;
            double nerf = Nerf(rating.Notes, 115, 20, 0.95) * Nerf(rating.NoteSpan.GetValueOrDefault(), 75, 12, 0.9);
            if (!modified && tilt == 0) return BaseTable.Select(p => (p.X, p.Y * nerf)).ToArray();
            var standard = new AnalyticCurve();
            var curve = new AnalyticCurve(Math.Min(Math.Max(row[1], 0.951), 1), Math.Min(row[2], 1), row[3], row[4], row[5], row[6]);
            var xs = new HashSet<double>(BaseTable.Select(p => p.X));
            for (int i = 0; i < 71; i++) xs.Add(Round6(0.6 + i * 0.005));
            for (int i = 0; i < 101; i++) xs.Add(Round6(0.95 + i * 0.0005));
            xs.Add(Round6(modified ? curve.A : 1));
            return xs.OrderBy(x => x).Select(x =>
            {
                double value = Interpolate(BaseTable, x);
                if (modified)
                {
                    double basis = SmoothValue(x), reference = standard.Value(x);
                    value = Math.Min(basis, reference > 0 ? basis * curve.Value(x) / reference : basis);
                }
                if (tilt != 0) value *= Math.Exp(tilt * Math.Min(Math.Max(x - prediction, -0.015), 0.015) * 100);
                return (x, value * nerf);
            }).ToArray();
        }
    }
}
