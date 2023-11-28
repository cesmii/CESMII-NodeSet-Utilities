# Usage

## Basic

Pass resolver to UANodeSetCacheManager constructor:
```c#
    var remoteServer = new TheRemoteUAServer();
    remoteServer.eventFired+= sinkEventFired;
    remoteServer.Validate(CreateInfo());
```

## Event Sink for Validator Output

```c#
    //txt contains the Validation Information
    //severity: 
    //0 = not set
    //1 = Info
    //2 = Warning
    //3 = Error
    //4 = Important Info
    void sinkEventFired(string txt, int severity)
    {
        Console.WriteLine(txt);
    }
```


## Create the Connection Info for the UA Client
```c#
    TheUAServerStates CreateInfo()
        {
            return new TheUAServerStates
            {
                //OPC Server Address
                Address = "opc.tcp://<myserver>", 

                //OPC Server Security Settings
                Anonymous = true|false,
                DisableSecurity = true|false,
                AcceptUntrustedCertificate = true|false,
                UserName = "<UserName>",
                Password = "<Password>",
                DisableDomainCheck = true|false,
                AcceptInvalidCertificate = true|false,

                //CloudLibrary Settings
                CloudLibEP = "https://<cloudlibUri>",
                CloudLibPWD = "<cloudLibUserName>",
                CloudLibUID = "<cloudLibPassword>",

                //Tracking Data
                LocalCachePath = "<folder where to cache nodeset files>",
                LocalTempPath = "<folder to store temp files>",
                LogLevel=0|1|2|3
            };
        }
```
