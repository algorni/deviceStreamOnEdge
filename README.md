# Device Stream IoT Edge Module
This repo contains an example of implementation of an Agent (a simple "Device Client") and an IoT Edge Module to establish a Device Stream.
Part of this repo is also a console app to initiate the Device Stream flow performing a Direct Method call to the remote end.

Both the Agent and the Module doesn't need to know the target host and port since those information is part of the Direct Method payload. 


Given to a limitation on the IoT Edge side (remember Device Stream as of today is still in Public Preview) in order to initiate the flow i've implemented, as a workaround, the use of an alternative Device Identity within the Module i built.
Even in this case the new alternative identity is not part of the Module configuration to reduce the complexity of the deployment of such module.
That identity will be provided as payload of the Direct Method call.

>Please remember, this is a quick and dirty example, take this as it is without any warrenty that this fits / works for your specifc use case.
Remember that give Device Stream is still in Public Preview is available without any specific SLA and with all the caviet of a Preview Service that you can find here: https://azure.microsoft.com/en-us/support/legal/preview-supplemental-terms/

---

## Repo Structure

Below you can find the Repo Structure.

![image](https://user-images.githubusercontent.com/45007019/125340804-32dbfa00-e353-11eb-972f-893f328866d7.png)

*DeviceStreamAgent* folder contains the source code of the "Device Agent" which initiate a DeviceClient and register for the Direct Method call.

*DeviceStreamCLI* folder contains the source code for the command line util to initiate the Device Stream via a Direct Method call to the target device (or module) and listen to the local port for tunneling the TCP connection.

*DeviceStreamCommon* folder contains a common class library with the definition of the Direct Method payload body and the shared code to initiate the Device Stream both on device side and on the "client app" side.

*DeviceStreamProxyModule* and the *Definition* folders contains the implementation of the IoT Edge Module.  

>Please consider that the IoT Edge module source refers to the Common class library therefore there is an etra "complexity" to build the related module, see the command below provided as a sample. 

---
### Device Agent

The Device Agent simply initiate a DeviceClient object, connects with the identity provided in its configuration (see below more details on configurations requirements) then it waits for a Direct Method call.

The Direct Method call initiate the Device Stream flow as described here: 

https://docs.microsoft.com/en-us/azure/iot-hub/iot-hub-device-streams-overview#device-stream-creation-flow 

If flow terminate the App will wait for another Direct Method call to re-establish a new Device Stream.

This application can run also a Docker Container, please see below how to build this container with Azure Container Register.

---
### IoT Edge Module

The IoT Edge Module leverate a ModuleClient to register for the Direct Method initiation call but currently doesn't support initiate the Device Stream flow directly.

As a __workaround__ i'm creating a new DeviceClient object with an alternative Device Identity to initiate the Davice Stream flow exactly as the Device Agent sample application does. 

---
### Command Line App

The command line app it connects to the IoT Hub Service endpoint and initiate the Direct Method call to start the Device Stream flow on the target device, once the Direct Method returns initiate also the Device Stream on "service side".
Finally it start to listen to a local TCP port for incoming TCP connections that will be served via the Device Stream flow up to the target host : port combination.

#### Direct Method Payload
The command line app initiate the Direct Method call with the follow payload which is defined into the class __InitiateDeviceStreamRequest__:
~~~ 
{
    "TargetHost":"<the hostname of the target host to which terminate the remote end TCP connection>",
    "TargetPort":<the target port to which terminate the remote-end TCP connection>,
    "USeDeviceConnectionString":"[OPTIONAL] The alternative Device Identity to use to initate the Device Stream on the device side"
}
~~~ 

>Those three attribute of the Direct Method Payload cames from the Command Line App configuration. 

---
### Docker build command

The file buildCommand.txt contains a sample of the Azure Container Registrer CLI command to build the IoT Edge Module & eventually the Device Agent.

>Given it refer multiple projects within the same solution there is a bit of extra complexity ther and you need to run this command from the root of this repository and not under the module project.

~~~ 
containerRegisterFQDN=<your container register FQDN>
deviceStreamModuleTag=<the device stream module container tag>
deviceStreamAgentTag=<the degice stream agent container tag>

az acr build --image $containerRegisterFQDN/devicestreammodule:$deviceStreamModuleTag --registry $containerRegisterName--file .\DeviceStreamProxyModule\Dockerfile.arm32v7 .

az acr build --image $containerRegisterFQDN/devicestreamagent:$deviceStreamAgentTag --registry $containerRegisterName--file .\DeviceStreamAgent\Dockerfile.arm32v7 .
~~~

>The exmaple provided is for an arm32 architecture, if you need to run on an amd64 architecture you can simply leverage the dockerfile for such architecture.

>The docker file to build the arm32 image was slight modified to use as build image an amd64 image releasing for linux-arm platform and using as runtime base image the arm32 .net core 5 runtime

~~~ 
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env
WORKDIR /app

COPY . ./
RUN dotnet publish DeviceStreamProxyModule -c Release -o out -r linux-arm

FROM mcr.microsoft.com/dotnet/runtime:5.0-buster-slim-arm32v7
WORKDIR /app
COPY --from=build-env /app/out ./

RUN useradd -ms /bin/bash moduleuser
USER moduleuser

ENTRYPOINT ["dotnet", "DeviceStreamProxyModule.dll"]
~~~

___
### Deployment of IoT Edge Module

The Deployment manifest of  your IoT Edge must inclue the refernece to your Container Register and the module reference:

    "deviceStream": {
                        "settings": {
                            "image": "<$containerRegisterFQDN>/devicestreamproxymodule:$deviceStreamModuleTag",
                            "createOptions": ""
                        },
                        "type": "docker",
                        "version": "1.0",
                        "status": "running",
                        "restartPolicy": "always"
                    }

As you can see no additional configuration needed as the necessary details will be provided by the companion CLI tool via the Direct Method call.

___

### Configurations

Configurations to the relevant app / module can be provided as CLI parameters, Environmental Variables or app settings file.

The code use the standard .NET Code feature to load an __Microsoft.Extensions.Configuration.IConfiguration__ object the the following code:


       configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

#### Device Agent 
The Device Stream Agent Line app requires just the Device Identity connection string to use to connect to the IoT Hub as a device.

| Paramter Name | Description |
| --- | ---| 
| DEVICE_CONNECTIONSTRING | Device Identity connection string to use to connect to the IoT Hub as a device |

#### IoT Edge Module
>The IoT Edge module doesn't require any specific configuration as it leverage the IoT Edge runtime to register the Direct Method callback.


#### Command Line App
The Command Line App requires several configuration including the IoT Hub Policy to connect to the service side endpoint to initiate the Direct Method call and the target device & target host information to initiate the Device Stream Flow see below table for additional details.

| Paramter Name | Description |
| --- | ---| 
| IOTHUB_CONNECTIONSTRING | The IoT Hub Policy connection string to connect a a service side application to IoT Hub using the Service SDK. |
| DEVICE_ID | The target Device ID that will initiate the Device Stream |
| MODULE_NAME | [OPTIONAL] The Module Name (in case of IoT Edge module) |
| ALTENRATIVE_DEVICE_ID |  [OPTIONAL] An alernative Device ID to use in case of IoT Edge installation (this device must be a plain "IoT Device", only SAS Key auth is supported from this sample) |
| ALTENRATIVE_DEVICE_CONNECTIONSTRING | [OPTIONAL] The alternative Device connection string (this will be used by the IoT Edge module to initiate a new DeviceClient objects and connection to IoT Hub for initiation of the Device Stream |
|LOCAL_PORT| The local TCP Port to which the App will start to listen for incoming TCP connection|
|REMOTE_HOST| The remote target host to which connect to|
|REMOTE_PORT| The remote target port to which connect to|

---

Feel free to fork / contribute / open git hub issue  and reach me out anytime in case you want additional help on the content of this repo!

My linkeding url: linkedin.com/in/gornialberto

