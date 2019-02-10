using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GossipMesh.Seed.Json
{
    class IPEndPointConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPEndPoint));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var ep = (IPEndPoint)value;
            writer.WriteValue(ep.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var ipEndPoint = reader.ReadAsString().Split(":");
            return new IPEndPoint(long.Parse(ipEndPoint[0]), int.Parse(ipEndPoint[1]));
        }
    }
}