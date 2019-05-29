using System;
using System.Net;
using Newtonsoft.Json;

namespace GossipMesh.SeedUI.Json
{
    public class DateTimeConverter : JsonConverter
    {
        private readonly string _dateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(DateTime));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((DateTime)value).ToString(_dateTimeFormat));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return DateTime.ParseExact((string)reader.Value, _dateTimeFormat, null);
        }
    }
}