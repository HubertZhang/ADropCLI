using System;
using System.IO;
using System.Net.Sockets;
using Google.Protobuf;

namespace ADrop
{
    public class Sender : IDisposable
    {
        Socket socket;
        Stream stream;
        CodedInputStream pbInputStream;
        CodedOutputStream pbOutputStream;

        public Sender(Socket socket)
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

        public void SendMetadata(Proto.MetaInfo metaInfo)
        {
            // pbOutputStream.WriteLength(metaInfo.CalculateSize());
            pbOutputStream.WriteMessage(metaInfo);
            pbOutputStream.Flush();
        }

        public Proto.Action WaitForConfirmation()
        {
            var action = new Proto.Action();
            pbInputStream.ReadMessage(action);
            return action;
        }

        public void SendData(byte[] data)
        {
            pbOutputStream.WriteLength(data.Length);
            pbOutputStream.Flush();
            stream.Write(data, 0, data.Length);
            stream.Flush();
            //pbOutputStream.WriteBytes(ByteString.CopyFrom(data));
            //pbOutputStream.Flush();

            var action = new Proto.Action();
            pbInputStream.ReadMessage(action);
        }

        public void SendData(FileStream file)
        {
            pbOutputStream.WriteLength((int)file.Length);
            pbOutputStream.Flush();
            file.CopyTo(stream);
            stream.Flush();
            var action = new Proto.Action();
            pbInputStream.ReadMessage(action);
        }
    }
}
