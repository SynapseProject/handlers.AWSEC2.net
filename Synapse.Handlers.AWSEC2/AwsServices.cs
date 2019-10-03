using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Synapse.Handlers.AWSEC2
{
    public class AwsServices
    {
        public static Instance GetInstance(string instanceId, AWSCredentials awsCredentials, AmazonEC2Config clientConfig)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new Exception("Instance id is not specified.");
            }

            if (awsCredentials == null)
            {
                throw new Exception("AWS credentials are not specified.");
            }

            if (clientConfig == null)
            {
                throw new Exception("Client config is not specified.");
            }

            List<Instance> instances = new List<Instance>();
            Instance foundInstance;

            try
            {
                using (AmazonEC2Client client = new AmazonEC2Client(awsCredentials, clientConfig))
                {
                    DescribeInstancesRequest req = new DescribeInstancesRequest
                    {
                        InstanceIds = { instanceId }
                    };
                    do
                    {
                        DescribeInstancesResponse resp = client.DescribeInstances(req);
                        if (resp != null)
                        {
                            instances.AddRange(resp.Reservations.SelectMany(reservation => reservation.Instances).Where(x => x.InstanceId == instanceId));
                            req.NextToken = resp.NextToken;
                        }
                    } while (!string.IsNullOrWhiteSpace(req.NextToken));
                }

                if (instances.Count == 1)
                {
                    foundInstance = instances[0];
                }
                else
                {
                    throw new Exception("Error finding the specified instance.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Encountered exception while describing EC2 instances: {ex.Message}");
            }

            return foundInstance;
        }

        public static void StopInstance(string instanceId, AWSCredentials awsCredentials, AmazonEC2Config clientConfig)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new Exception("Instance id is not specified.");
            }

            if (awsCredentials == null)
            {
                throw new Exception("AWS credentials are not specified.");
            }

            if (clientConfig == null)
            {
                throw new Exception("Client config is not specified.");
            }

            try
            {
                using (AmazonEC2Client client = new AmazonEC2Client(awsCredentials, clientConfig))
                {
                    StopInstancesRequest req = new StopInstancesRequest
                    {
                        InstanceIds = new List<string>() { instanceId }
                    };
                    client.StopInstances(req);
                }
            }
            catch (AmazonEC2Exception ex)
            {
                // Check the ErrorCode to see if the instance does not exist.
                if ("InvalidInstanceID.NotFound" == ex.ErrorCode)
                {
                    throw new Exception($"EC2 instance {instanceId} does not exist.");
                }
                // The exception was thrown for another reason, so re-throw the exception.
                throw;
            }
        }

        public static void StartInstance(string instanceId, AWSCredentials awsCredentials, AmazonEC2Config clientConfig)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new Exception("Instance id is not specified.");
            }

            if (awsCredentials == null)
            {
                throw new Exception("AWS credentials are not specified.");
            }

            if (clientConfig == null)
            {
                throw new Exception("Client config is not specified.");
            }

            try
            {
                using (AmazonEC2Client client = new AmazonEC2Client(awsCredentials, clientConfig))
                {
                    StartInstancesRequest req = new StartInstancesRequest
                    {
                        InstanceIds = new List<string>() { instanceId }
                    };
                    client.StartInstances(req);
                }
            }
            catch (AmazonEC2Exception ex)
            {
                // Check the ErrorCode to see if the instance does not exist.
                if ("InvalidInstanceID.NotFound" == ex.ErrorCode)
                {
                    throw new Exception($"EC2 instance {instanceId} does not exist.");
                }

                // The exception was thrown for another reason, so re-throw the exception.
                throw;
            }
        }
    }
}
