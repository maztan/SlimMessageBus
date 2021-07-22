namespace SlimMessageBus.Host
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    public static class MessageHeaderExtensions
    {
        public static void SetHeader(this IDictionary<string, string> headers, string header, string value)
        {
            if (header is null) throw new ArgumentNullException(nameof(headers));

            if (value != null)
            {
                headers[header] = value;
            }
            else
            {
                headers.Remove(header);
            }
        }

        public static void SetHeader(this IDictionary<string, string> headers, string header, int value) =>
            headers.SetHeader(header, value.ToString(CultureInfo.InvariantCulture));

        public static void SetHeader(this IDictionary<string, string> headers, string header, DateTimeOffset dt) =>
            headers.SetHeader(header, dt.ToFileTime().ToString(CultureInfo.InvariantCulture));

        public static int GetHeaderAsInt(this IDictionary<string, string> headers, string header)
        {
            if (header is null) throw new ArgumentNullException(nameof(headers));

            var v = headers[header];
            return int.Parse(v, CultureInfo.InvariantCulture);
        }

        public static bool TryGetHeader(this IDictionary<string, string> headers, string header, out string value) =>
            headers.TryGetValue(header, out value);

        public static bool TryGetHeader(this IDictionary<string, string> headers, string header, out DateTimeOffset dt)
        {
            if (header != null && headers.TryGetValue(header, out var dtStr))
            {
                var dtLong = long.Parse(dtStr, CultureInfo.InvariantCulture);
                dt = DateTimeOffset.FromFileTime(dtLong);
                return true;
            }
            dt = default;
            return false;
        }

        public static bool TryGetHeader(this IDictionary<string, string> headers, string header, out DateTimeOffset? dt)
        {
            if (headers.TryGetHeader(header, out DateTimeOffset dt2))
            {
                dt = dt2;
                return true;
            }
            dt = null;
            return false;
        }
    }
}