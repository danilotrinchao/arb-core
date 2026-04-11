namespace Arb.Core.Infrastructure.Redis
{
    public class RedisOptions
    {
        public const string SectionName = "Redis";
        public string Connection { get; init; } /*= "localhost:6379";*/
    }
}
