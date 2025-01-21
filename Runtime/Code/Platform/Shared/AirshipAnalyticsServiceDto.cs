using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Code.Platform.Shared {
    [Serializable]
    public class ActivePackage{
        // The name of the package, e.g. "@Easy/Core"
        public string name;
        // The version of the package, e.g. "0.1.0"
        public string version;
    }

    [Serializable]
    public class ReportableError{
        // The timestamp of the error, ISO 8601 format, e.g. "2022-01-01T00:00:00.000Z"
        public string timestamp;
        public string stackTrace;
        public string message;
    }

    [Serializable]
    public class AirshipAnalyticsServerDto {
        // The version id of the game, e.g. 123
        public string gameVersionId;
        // The player version id of the server
        public string playerVersionId;
        // The list of active packages currently installed on the server
        public List<ActivePackage> activePackages;
        // The list of lua errors that have occurred on the server
        public List<ReportableError> errors;

    }


    [Serializable]
    public class AirshipAnalyticsClientDto {
        public string gameId;
        // The version id of the game, e.g. 123
        public string gameVersionId;
        // The id of the currently connected server
        public string serverId;
        // The player version id of the client
        public string playerVersionId;
        // The list of active packages currently installed on the server
        public List<ActivePackage> activePackages;
        // The list of lua errors that have occurred on the server
        public List<ReportableError> errors;

    }
}