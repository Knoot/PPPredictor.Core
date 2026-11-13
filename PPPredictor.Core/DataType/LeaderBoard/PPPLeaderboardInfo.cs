using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core.DataType.LeaderBoard
{
    public class PPPLeaderboardInfo
    {
        private Leaderboard _leaderboard;
        private string _leaderboardName;
        private string _leaderboardIcon;
        private string _ppSuffix;
        private int _leaderboardFirstPageIndex;
        private bool _isCountryRankEnabled;
        private int _largePageSize;
        private int _playerPerPages = 0;
        private bool _hasGetAllScoresFunctionality = false;
        private bool _hasGetRecentScoresFunctionality = true;
        private bool _hasPPToRankFunctionality = false;
        private int _taskDelayValue = 250;
        private bool _hasOldDotRanking = true;
        private int _pageFetchLimit = 5;

        public Leaderboard Leaderboard { get => _leaderboard; set => _leaderboard = value; }
        public string LeaderboardName { get => _leaderboardName; }
        public string LeaderboardIcon { get => _leaderboardIcon; }
        public string PpSuffix { get => _ppSuffix; }
        public int LeaderboardFirstPageIndex { get => _leaderboardFirstPageIndex; }
        public bool IsCountryRankEnabled { get => _isCountryRankEnabled; }
        public int LargePageSize { get => _largePageSize; }
        public int PlayerPerPages { get => _playerPerPages; }
        public bool HasGetAllScoresFunctionality { get => _hasGetAllScoresFunctionality; }
        public bool HasGetRecentScoresFunctionality { get => _hasGetRecentScoresFunctionality; }
        public bool HasPPToRankFunctionality { get => _hasPPToRankFunctionality; }
        public int TaskDelayValue { get => _taskDelayValue; }
        public bool HasOldDotRanking { get => _hasOldDotRanking; }
        public int PageFetchLimit { get => _pageFetchLimit; }

        public PPPLeaderboardInfo()
        {
        }

        public PPPLeaderboardInfo(Leaderboard leaderboard)
        {
            this._leaderboard = leaderboard;
            this._leaderboardName = leaderboard.ToString();
            this._ppSuffix = "pp";
            _leaderboardFirstPageIndex = 1;
            _isCountryRankEnabled = true;
            _largePageSize = 10;

            switch (leaderboard)
            {
                case Leaderboard.ScoreSaber:
                    _leaderboardIcon = "PPPredictor.Resources.LeaderBoardLogos.ScoreSaber.png";
                    _largePageSize = 100;
                    _playerPerPages = 50;
                    _hasOldDotRanking = false;
                    break;
                case Leaderboard.BeatLeader:
                    _leaderboardIcon = "PPPredictor.Resources.LeaderBoardLogos.BeatLeader.png";
                    _largePageSize = 100;
                    _playerPerPages = 50;
                    _taskDelayValue = 1100;
                    break;
                case Leaderboard.NoLeaderboard:
                    _leaderboardIcon = "PPPredictor.Resources.LeaderBoardLogos.Empty.png";
                    break;
                case Leaderboard.HitBloq:
                    _leaderboardIcon = "PPPredictor.Resources.LeaderBoardLogos.HitBloq.png";
                    _ppSuffix = "cr";
                    _isCountryRankEnabled = false;
                    _leaderboardFirstPageIndex = 0;
                    _playerPerPages = 10;
                    _hasGetAllScoresFunctionality = true;
                    _hasPPToRankFunctionality = true;
                    break;
                case Leaderboard.AccSaber:
                    _leaderboardIcon = "PPPredictor.Resources.LeaderBoardLogos.AccSaber.png";
                    _ppSuffix = "ap";
                    _isCountryRankEnabled = false;
                    _leaderboardFirstPageIndex = 0;
                    _hasGetAllScoresFunctionality = true;
                    _hasGetRecentScoresFunctionality = false;
                    break;
            }
        }
    }
}
