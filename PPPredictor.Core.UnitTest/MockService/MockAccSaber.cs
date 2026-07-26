using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.Interface;

namespace PPPredictor.Core.UnitTest.MockService
{
    internal class MockAccSaber : IAccSaberAPI
    {
        public Task<List<AccSaberDataTypes.AccSaberMapPool>> GetAccSaberMapPools()
        {
            throw new NotImplementedException();
        }

        public Task<AccSaberDataTypes.AccSaberPlayer> GetAccSaberUserByPool(long userId, string poolIdent)
        {
            throw new NotImplementedException();
        }

        public Task<List<AccSaberDataTypes.AccSaberRankedMap>> GetAllRankedMaps()
        {
            throw new NotImplementedException();
        }

        public Task<AccSaberDataTypes.AccSaberMap> GetMapByHash(string hash)
        {
            throw new NotImplementedException();
        }

        public Task<List<AccSaberDataTypes.AccSaberScores>> GetAllScores(string userId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AccSaberDataTypes.AccSaberScores>> GetAllScoresByPool(string userId, string poolId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AccSaberDataTypes.AccSaberPlayer>> GetPlayerListForMapPool(double page, string mapPoolId)
        {
            throw new NotImplementedException();
        }

        public Task<List<AccSaberDataTypes.AccSaberRankedMap>> GetRankedMaps(string mapPool)
        {
            throw new NotImplementedException();
        }
    }
}
