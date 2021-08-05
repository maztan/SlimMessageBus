namespace SlimMessageBus.Host
{
    using System.Collections.Generic;

    public struct MessageWithHeaders
    {
        public byte[] Payload { get; }
        public IDictionary<string, object> Headers { get; }

        public MessageWithHeaders(byte[] payload)
            : this(payload, new Dictionary<string, object>())
        {
        }

        public MessageWithHeaders(byte[] payload, IDictionary<string, object> headers)
        {
            Headers = headers;
            Payload = payload;
        }
    }
}