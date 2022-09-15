﻿using System.Collections.Generic;
using System.Linq;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp.Dao;

internal class LabCredentialDao : Dao<LabCredential>
{
    public LabCredentialDao(Config config, ILogger logger) : base(config, logger)
    {
    }

    public List<LabCredential> GetByProject(string project)
    {
        var oDataQueryEntities =
            TableClient.Query<LabCredential>(c => c.PartitionKey == project);

        return oDataQueryEntities.OrderBy(c => c.Email).ToList();
    }
}