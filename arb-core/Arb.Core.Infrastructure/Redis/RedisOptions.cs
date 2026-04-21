namespace Arb.Core.Infrastructure.Redis
{
    public class RedisOptions
    {
        public const string SectionName = "Redis";
        public string Connection { get; set; } /*= "localhost:6379";*/
    }
}
