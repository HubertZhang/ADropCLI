using Makaretu.Dns;

using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace ADrop
{
    class Util
    {
        public static async Task<SRVRecord> Resolve(DomainName server, CancellationToken token = default)
        {

            var query = new Message
            {
                Opcode = MessageOperation.Query,
                QR = false
            };
            query.Questions.Add(new Question { Name = server, Type = DnsType.SRV });
            var mdns = new MulticastService();
            mdns.Start();
            var target = await mdns.ResolveAsync(query, token);
            mdns.Stop();
            return target.Answers.OfType<SRVRecord>().First();
        }
    }
}