using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeviceStreamCommon
{
    public class InitiateDeviceStreamResponse
    {
        public bool RequestAccepted { get; set; }

        public string Reason { get; set; }

        public byte[] GetJsonByte()
        {
            var jsonRepp = JsonConvert.SerializeObject(this,Formatting.Indented);

            byte[] bytes = Encoding.UTF8.GetBytes(jsonRepp);

            return bytes;
        }

        public static InitiateDeviceStreamResponse FromJson(string dataAsJson)
        {
            InitiateDeviceStreamResponse instance = JsonConvert.DeserializeObject<InitiateDeviceStreamResponse>(dataAsJson);

            return instance;
        }
    }
}
