using System;
using System.Net;
using GossipMesh.Core;
using Newtonsoft.Json;

namespace GossipMesh.Seed.Json
{
    public class MemberStateConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(MemberState));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return (MemberState)reader.Value;
        }
    }
}