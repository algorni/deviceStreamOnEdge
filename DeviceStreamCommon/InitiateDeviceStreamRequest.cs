using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeviceStreamCommon
{
    public class InitiateDeviceStreamRequest
    {
        public string TargetHost { get; set; }
        public int TargetPort { get; set; }

        /// <summary>
        /// Use a different Device Client to connect to the Stream
        /// </summary>
        public string UseDeviceConnectionString { get; set; }

        /// <summary>
        /// static ctor
        /// </summary>
        /// <param name="dataAsJson"></param>
        /// <returns></returns>
        public static InitiateDeviceStreamRequest FromJson(string dataAsJson)
        {
            InitiateDeviceStreamRequest instance = JsonConvert.DeserializeObject<InitiateDeviceStreamRequest>(dataAsJson);

            return instance;
        }

        public string ToJson()
        {
            var jsonRepp = JsonConvert.SerializeObject(this, Formatting.Indented);

            return jsonRepp;
        }
    }
}
