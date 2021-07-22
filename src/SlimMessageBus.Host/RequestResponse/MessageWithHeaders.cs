namespace SlimMessageBus.Host
{
    using System.Collections.Generic;

    public struct MessageWithHeaders
    {
        public byte[] Payload { get; }
        public IDictionary<string, string> Headers { get; }

        public MessageWithHeaders(byte[] payload)
            : this(payload, new Dictionary<string, string>())
        {
        }

        public MessageWithHeaders(byte[] payload, IDictionary<string, string> headers)
        {
            Headers = headers;
            Payload = payload;
        }
    }
}