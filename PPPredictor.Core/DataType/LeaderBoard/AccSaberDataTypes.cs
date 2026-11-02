using System;
using System.Collections.Generic;

namespace PPPredictor.Core.DataType.LeaderBoard
{
    internal class AccSaberDataTypes
    {
        internal class AccSaberScores
        {
            public double ap { get; set; }
            public double weightedAp { get; set; }
            public long? scoreNoMods { get; set; }
            public string songHash { get; set; }
            public string difficulty { get; set; }
            public DateTime timeSet { get; set; }
            public string categoryDisplayName { get; set; }
            public string categoryId { get; set; }
        }

        internal class AccSaberPlayer
        {
            public int rank { get; set; }
            public double ap { get; set; }
        }

        internal class AccSaberScorePage
        {
            public List<AccSaberScores> content { get; set; }
            public bool last { get; set; }
        }

        internal class AccSaberStatisticsCollection
        {
            public List<AccSaberStatistics> categories { get; set; }
        }

        internal class AccSaberStatistics
        {
            public string categoryId { get; set; }
            public int ranking { get; set; }
            public double ap { get; set; }
        }

        internal class AccSaberMap
        {
            public string songHash { get; set; }
            public List<AccSaberMapDifficulty> difficulties { get; set; }
        }

        internal class AccSaberMapDifficulty
        {
            public string categoryId { get; set; }
            public string difficulty { get; set; }
            public double complexity { get; set; }
        }

        internal class AccSaberMapPool
        {
            public string categoryName { get; set; }
            public string categoryDisplayName { get; set; }
            public string id { get; set; }
            public string code { get; set; }
            public string name { get; set; }
        }

        internal class AccSaberRankedMap
        {
            public string difficulty { get; set; }
            public string songHash { get; set; }
            public double complexity { get; set; }
            public string categoryDisplayName { get; set; }
        }
    }
}
