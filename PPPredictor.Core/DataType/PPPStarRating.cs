using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace PPPredictor.Core.DataType
{
    public class PPPStarRating
    {
        private double _stars = 0;
        private double _multiplier = 1;
        private double _predictedAcc = 0;
        private double _passRating = 0;
        private double _accRating = 0;
        private double _techRating = 0;
        private bool? _rankedBeatLeader = null;
        private Dictionary<string, double> _modifiersRating = new Dictionary<string, double>();
        private Dictionary<string, double> _modifierValues = new Dictionary<string, double>();
        
        [DefaultValue(null)]
        public bool? RankedBeatLeader{ get => _rankedBeatLeader; set => _rankedBeatLeader = value; }
        [DefaultValue(0d)]
        public double Stars { get => _stars; set => _stars = value; }
        [DefaultValue(0d)]
        public double PredictedAcc { get => _predictedAcc; set => _predictedAcc = value; }
        public double SliderPercentage { get; set; }
        public int Notes { get; set; }
        public double? NoteSpan { get; set; }
        public int? BeatLeaderServerPPVersion { get; set; }
        [DefaultValue(0d)]
        public double PassRating { get => _passRating; set => _passRating = value; }
        [DefaultValue(0d)] 
        public double AccRating { get => _accRating; set => _accRating = value; }
        [DefaultValue(0d)]
        public double TechRating { get => _techRating; set => _techRating = value; }
        [DefaultValue(null)]
        public Dictionary<string, double> ModifiersRating { get => _modifiersRating; set => _modifiersRating = value; }
        [DefaultValue(null)]
        public Dictionary<string, double> ModifierValues { get => _modifierValues; set => _modifierValues = value; }
        [JsonIgnoreAttribute]
        [DefaultValue(1)]
        public double Multiplier { get => _multiplier; set => _multiplier = value; }

        internal PPPStarRating()
        {
        }

        public PPPStarRating(double starRating)
        {
            _stars = _predictedAcc = _passRating = _accRating = _techRating = starRating;
        }

        internal PPPStarRating(LeaderBoard.BeatLeaderDataTypes.BeatLeaderDifficulty beatLeaderDifficulty, bool? isEventRanked = null)
        {
            if (isEventRanked.HasValue)
            {
                _rankedBeatLeader = isEventRanked.Value;
            }
            else
            {
                _rankedBeatLeader = beatLeaderDifficulty.status == (int)Enums.BeatLeaderDifficultyStatus.ranked;
            }
            _predictedAcc = beatLeaderDifficulty.predictedAcc.GetValueOrDefault();
            _stars = beatLeaderDifficulty.stars.GetValueOrDefault();
            SliderPercentage = beatLeaderDifficulty.sliderPercentage.GetValueOrDefault();
            Notes = beatLeaderDifficulty.notes;
            NoteSpan = beatLeaderDifficulty.noteSpan;
            BeatLeaderServerPPVersion = beatLeaderDifficulty.ppVersion;
            _passRating = beatLeaderDifficulty.passRating.GetValueOrDefault();
            _accRating = beatLeaderDifficulty.accRating.GetValueOrDefault();
            _techRating = beatLeaderDifficulty.techRating.GetValueOrDefault();
            _modifiersRating = beatLeaderDifficulty.modifiersRating ?? null;
            _modifierValues = beatLeaderDifficulty.modifierValues ?? null;
        }

        internal PPPStarRating(double mulitplier, double accRating, double passRating, double techRating, bool? rankedBeatLeader)
        {
            _rankedBeatLeader = rankedBeatLeader;
            _multiplier = mulitplier;
            _accRating = accRating;
            _passRating = passRating;
            _techRating = techRating;
        }

        public bool IsRanked()
        {
            if(_rankedBeatLeader == false){
                return false;
            }

            return _predictedAcc > 0 || _passRating > 0 || _accRating > 0 || _techRating > 0 || _stars > 0;      
        }

        internal bool HasSameRatings(PPPStarRating other)
        {
            return other != null && IsRanked() == other.IsRanked() && Stars == other.Stars
                && PredictedAcc == other.PredictedAcc && AccRating == other.AccRating && PassRating == other.PassRating
                && TechRating == other.TechRating && SliderPercentage == other.SliderPercentage
                && Notes == other.Notes && NoteSpan == other.NoteSpan && BeatLeaderServerPPVersion == other.BeatLeaderServerPPVersion
                && SameValues(ModifiersRating, other.ModifiersRating) && SameValues(ModifierValues, other.ModifierValues);
        }

        internal PPPStarRating Copy()
        {
            var copy = (PPPStarRating)MemberwiseClone();
            copy.ModifierValues = ModifierValues == null ? null : new Dictionary<string, double>(ModifierValues, ModifierValues.Comparer);
            copy.ModifiersRating = ModifiersRating == null ? null : new Dictionary<string, double>(ModifiersRating, ModifiersRating.Comparer);
            return copy;
        }

        private static bool SameValues(Dictionary<string, double> first, Dictionary<string, double> second)
        {
            if ((first?.Count ?? 0) != (second?.Count ?? 0)) return false;
            if (first == null || first.Count == 0) return true;
            foreach (var pair in first)
                if (!second.TryGetValue(pair.Key, out double value) || value != pair.Value) return false;
            return true;
        }
    }
}
