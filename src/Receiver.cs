using System;
using System.IO;
using System.Net.Sockets;
using Google.Protobuf;
using System.Threading;
using System.Threading.Tasks;

namespace ADrop
{

    public class Receiver : IDisposable
    {
        Socket socket;
        Stream stream;
        CodedInputStream pbInputStream;
        CodedOutputStream pbOutputStream;

        public Receiver(Socket socket)
        {
            this.socket = socket;
            stream = new NetworkStream(socket, true);
            pbInputStream = new CodedInputStream(stream, true);
            pbOutputStream = new CodedOutputStream(stream, true);
        }

        public void Dispose()
        {
            pbInputStream.Dispose();
            pbOutputStream.Dispose();
            stream.Dispose();
            socket.Close();
        }

        public Proto.MetaInfo ReadMetadata()
        {
            var metaInfo = new Proto.MetaInfo();
            pbInputStream.ReadMessage(metaInfo);
            return metaInfo;
        }

        public void SendAction(Proto.Action.Types.ActionType actionType)
        {
            pbOutputStream.WriteMessage(new Proto.Action { Type = actionType });
            pbOutputStream.Flush();
        }

        public void SendAction(Proto.Action action)
        {
            pbOutputStream.WriteMessage(action);
            pbOutputStream.Flush();
        }

        public async Task<byte[]> ReadFile(CancellationToken token = default)
        {
            var length = pbInputStream.ReadLength();
            var readed = 0;
            var result = new byte[length];
            while (length - readed > 0)
            {
                var r = await stream.ReadAsync(result, readed, length - readed, token);
                readed += r;
            }
            pbOutputStream.WriteMessage(new Proto.Action { Type = Proto.Action.Types.ActionType.Default });
            pbOutputStream.Flush();
            return result;
        }
    }
}
