﻿using InfluxData.Net.InfluxDb.Models.Responses;
using System.Collections.Generic;

namespace InfluxData.Net.InfluxDb.ResponseParsers
{
    public interface ISerieResponseParser
    {
        IEnumerable<SerieSet> GetSerieSets(IEnumerable<Serie> series);

        IEnumerable<Measurement> GetMeasurements(IEnumerable<Serie> series);

        IEnumerable<string> GetTagKeys(IEnumerable<Serie> series);

        IEnumerable<TagValue> GetTagValues(IEnumerable<Serie> series);

        IEnumerable<FieldKey> GetFieldKeys(IEnumerable<Serie> series);
    }
}