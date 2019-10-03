using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Synapse.Core;
using Synapse.Handlers.AWSEC2;
using System;
using System.Collections.Generic;
using System.Threading;
using StatusType = Synapse.Core.StatusType;

public class AwsEc2Handler : HandlerRuntimeBase
{
    private HandlerConfig _config;
    private string _progressMessage = "";
    private int _sequenceNumber = 0;
    private string _context = "Execute";
    private EC2Response _response = null;
    private UserRequest _request = null;
    private ExecuteResult _result = new ExecuteResult()
    {
        Status = Synapse.Core.StatusType.None,
        BranchStatus = Synapse.Core.StatusType.None,
        Sequence = int.MaxValue
    };

    public override ExecuteResult Execute(HandlerStartInfo startInfo)
    {
        try
        {
            UpdateProgress("Parsing incoming request...", StatusType.Running);
            _request = DeserializeOrDefault<UserRequest>(startInfo.Parameters);

            UpdateProgress("Executing request" + (startInfo.IsDryRun ? " in dry run mode..." : "..."), StatusType.Running);
            if (ValidateRequest(_request))
            {
                if (!startInfo.IsDryRun)
                {
                    _response = RunEC2Command(_request, _config);
                    UpdateProgress("Execution is completed.", StatusType.Complete, int.MaxValue);
                    if (_response == null)
                    {
                        _response = new EC2Response
                        {
                            Status = "Failed"
                        };
                    }
                    _response.Summary = _progressMessage;
                }
                else
                {
                    UpdateProgress("Dry run execution is completed.", StatusType.Complete, int.MaxValue);
                }
            }
        }
        catch (Exception ex)
        {
            string errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            UpdateProgress($"Execution has been aborted due to: {errorMessage}", StatusType.Failed, int.MaxValue, LogLevel.Error, ex);
            _response = new EC2Response()
            {
                Status = "Failed",
                Summary = _progressMessage
            };
        }

        _result.ExitData = _response;
        return _result;
    }

    public EC2Response RunEC2Command(UserRequest request, HandlerConfig config)
    {
        if (request == null || config == null) return null;
        string errorMessage = string.Empty;
        EC2Response output = new EC2Response()
        {
            Status = "Failed" // If no processing is done.
        };

        int maximumWaitTime = config.MaximumWaitTime; // In mili-seconds

        AmazonEC2Config clientConfig = new AmazonEC2Config()
        {
            MaxErrorRetry = config.ClientMaxErrorRetry,
            Timeout = TimeSpan.FromSeconds(config.ClientTimeoutSeconds),
            ReadWriteTimeout = TimeSpan.FromSeconds(config.ClientReadWriteTimeoutSeconds),
            RegionEndpoint = RegionEndpoint.GetBySystemName(request.AwsRegion) // Or RegionEndpoint.EUWest1
        };

        try
        {
            // https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/net-dg-config-creds.html
            // Accessing Credentials and Profiles in an Application
            CredentialProfileStoreChain chain = new CredentialProfileStoreChain(config.AwsProfilesLocation);

            AWSCredentials awsCredentials;
            if (chain.TryGetAWSCredentials(request.AwsRole, out awsCredentials))
            {
                // Use awsCredentials
                if (request.InstanceAction.ToLower() == "stop")
                {
                    try
                    {
                        AwsServices.StopInstance(request.InstanceId, awsCredentials, clientConfig);

                        // Wait for instance to stop
                        string state;
                        int counter = 5000;

                        do
                        {
                            UpdateProgress("Waiting for EC2 to be stopped...", StatusType.Running);
                            Thread.Sleep(5000);
                            var instance = AwsServices.GetInstance(request.InstanceId, awsCredentials, clientConfig);
                            state = instance.State.Name.Value;
                            counter += 5000;
                        } while (state != "stopped" && state != "terminated" && counter < maximumWaitTime);

                        output.Status = "Complete";
                        output.ErrorMessage = errorMessage;
                        output.InstanceName = request.InstanceName;
                        output.InstanceId = request.InstanceId;
                        output.InstanceState = state;
                    }
                    catch (AmazonEC2Exception ex)
                    {
                        // Check the ErrorCode to see if the instance does not exist.
                        if ("InvalidInstanceID.NotFound" == ex.ErrorCode)
                        {
                            throw new Exception($"EC2 instance {request.InstanceId} does not exist.");
                        }
                        // The exception was thrown for another reason, so re-throw the exception.
                        throw;
                    }
                }
                else if (request.InstanceAction.ToLower() == "start")
                {
                    try
                    {
                        AwsServices.StartInstance(request.InstanceId, awsCredentials, clientConfig);

                        // Wait for instance to start
                        string state;
                        int counter = 5000;

                        do
                        {
                            UpdateProgress("Waiting for EC2 to be started...", StatusType.Running);
                            Thread.Sleep(5000);
                            var instance = AwsServices.GetInstance(request.InstanceId, awsCredentials, clientConfig);
                            state = instance.State.Name.Value;
                            counter += 5000;
                        } while (state != "running" && state != "terminated" && counter < maximumWaitTime);

                        output.Status = "Complete";
                        output.ErrorMessage = errorMessage;
                        output.InstanceId = request.InstanceId;
                        output.InstanceName = request.InstanceName;
                        output.InstanceState = state;
                    }
                    catch (AmazonEC2Exception ex)
                    {
                        // Check the ErrorCode to see if the instance does not exist.
                        if ("InvalidInstanceID.NotFound" == ex.ErrorCode)
                        {
                            throw new Exception($"EC2 instance {request.InstanceId} does not exist.");
                        }
                        // The exception was thrown for another reason, so re-throw the exception.
                        throw;
                    }
                }
            }
            else
            {
                errorMessage = "AWS credentials cannot be found for the execution.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new Exception(errorMessage);
        }
        return output;
    }

    public static bool ValidateRequest(UserRequest request)
    {
        string errorMessage = string.Empty;

        if (request == null)
        {
            errorMessage = "Request cannot be null or empty.";
        }
        else if (string.IsNullOrWhiteSpace(request.InstanceId))
        {
            errorMessage = "Instance id cannot be null or empty.";
        }
        else if (string.IsNullOrWhiteSpace(request.InstanceName))
        {
            errorMessage = "Instance name cannot be null or empty.";
        }
        else if (string.IsNullOrWhiteSpace(request.InstanceAction))
        {
            errorMessage = "Instance action cannot be null or empty";
        }
        else if (request.InstanceAction.ToLower() != "stop" && request.InstanceAction.ToLower() != "start")
        {
            errorMessage = "Instance action can only be 'Stop' or 'Start'";
        }
        else if (!IsAwsRegion(request.AwsRegion))
        {
            errorMessage = "AWS region specified is not valid.";
        }
        else if (string.IsNullOrWhiteSpace(request.AwsRole))
        {
            errorMessage = "AWS role cannot be null or empty.";
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new Exception(errorMessage);
        }
        return true;
    }

    public static bool IsAwsRegion(string region)
    {
        if (region == null) return false;

        return RegionEndpoint.GetBySystemName(region).DisplayName != "Unknown";
    }

    public override object GetConfigInstance()
    {
        return new HandlerConfig
        {
            ClientMaxErrorRetry = 10,
            ClientTimeoutSeconds = 120,
            ClientReadWriteTimeoutSeconds = 120
        };
    }

    public override IHandlerRuntime Initialize(string values)
    {
        try
        {
            _config = DeserializeOrNew<HandlerConfig>(values) ?? new HandlerConfig();
        }
        catch (Exception ex)
        {
            OnLogMessage("Initialization", "Encountered exception while deserializing handler config.", LogLevel.Error, ex);
        }

        return this;
    }

    public override object GetParametersInstance()
    {
        return new UserRequest
        {
            InstanceId = "i-12345678",
            InstanceName = "XXXXXX",
            InstanceAction = "Stop",
            AwsRegion = "eu-west-1",
            AwsRole = "xxxxxxxx"
        };
    }

    private void UpdateProgress(string message, StatusType status, int sequenceNumber = 0, LogLevel logLevel = LogLevel.Info, Exception ex = null)
    {
        _progressMessage = $"{DateTime.UtcNow} {message}";
        _result.Status = status;
        _sequenceNumber = sequenceNumber == 0 ? _sequenceNumber++ : sequenceNumber;
        OnProgress(_context, _progressMessage, _result.Status, _sequenceNumber);
        OnLogMessage(_context, _progressMessage, logLevel, ex);
    }
}


public class HandlerConfig
{
    public string AwsProfilesLocation { get; set; }

    // https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/retries-timeouts.html
    public int ClientMaxErrorRetry { get; set; } = 4;

    public int ClientTimeoutSeconds { get; set; } = 100;

    public int ClientReadWriteTimeoutSeconds { get; set; } = 300;

    public int MaximumWaitTime { get; set; } = 5000 * 60;
}

public class UserRequest
{
    public string InstanceId { get; set; }

    public string InstanceName { get; set; }

    public string InstanceAction { get; set; } // Stop or Start

    public string AwsRegion { get; set; }

    public string AwsRole { get; set; }
}

public class EC2Response
{
    public string Status { get; set; } // "Complete", "Failed"

    public string ErrorMessage { get; set; }

    public string Summary { get; set; } // Handler Execution Summary

    public string InstanceId { get; set; }

    public string InstanceState { get; set; } // pending | running | shutting-down | terminated | stopping | stopped

    public string InstanceName { get; set; }
}