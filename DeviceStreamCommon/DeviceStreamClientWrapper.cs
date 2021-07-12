using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamCommon
{
    /// <summary>
    ///  Both Module Client and Device Client expose Device Stream functionality.
    //   Just biuld a wrapper around this iot hub -ish client 
    /// </summary>
    public class DeviceStreamClientWrapper
    { 
        private ModuleClient _moduleClient = null;
        private DeviceClient _deviceClient = null;

        public DeviceStreamClientWrapper(ModuleClient moduleClient)
        {
            this._moduleClient = moduleClient;
        }

        public DeviceStreamClientWrapper(DeviceClient deviceClient)
        {
            this._deviceClient = deviceClient;
        }

        public async Task<DeviceStreamRequest> WaitForDeviceStreamRequestAsync(CancellationToken token)
        {
            if (this._deviceClient != null)
            {
                return await this._deviceClient.WaitForDeviceStreamRequestAsync(token);
            }

            if (this._moduleClient != null)
            {
                return await this._moduleClient.WaitForDeviceStreamRequestAsync(token);
            }

            return null;
        }

        public async Task AcceptDeviceStreamRequestAsync(DeviceStreamRequest streamRequest, CancellationToken token)
        {
            if (this._deviceClient != null)
            {
                await this._deviceClient.AcceptDeviceStreamRequestAsync(streamRequest, token);
            }

            if (this._moduleClient != null)
            {
                await this._moduleClient.AcceptDeviceStreamRequestAsync(streamRequest, token);
            }
        }
    }
}
