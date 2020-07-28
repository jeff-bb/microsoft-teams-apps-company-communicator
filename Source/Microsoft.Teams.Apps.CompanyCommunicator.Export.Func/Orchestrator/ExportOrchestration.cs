﻿// <copyright file="ExportOrchestration.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Orchestrator
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.DurableTask;
    using Microsoft.Extensions.Logging;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.ExportData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Activities;
    using Microsoft.Teams.Apps.CompanyCommunicator.Export.Func.Model;

    /// <summary>
    /// This class is the durable framework orchestration for exporting notifications.
    /// </summary>
    public class ExportOrchestration
    {
        private readonly UploadActivity uploadActivity;
        private readonly SendFileCardActivity sendFileCardActivity;
        private readonly GetMetaDataActivity getMetaDataActivity;
        private readonly UpdateExportDataActivity updateExportDataActivity;
        private readonly HandleFailureActivity handleFailureActivity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportOrchestration"/> class.
        /// </summary>
        /// <param name="uploadActivity">upload zip activity.</param>
        /// <param name="sendFileCardActivity">send file card activity.</param>
        /// <param name="getMetaDataActivity">get the metadata activity.</param>
        /// <param name="updateExportDataActivity">update the export data activity.</param>
        /// <param name="handleFailureActivity">handle failure activity.</param>
        public ExportOrchestration(
            UploadActivity uploadActivity,
            SendFileCardActivity sendFileCardActivity,
            GetMetaDataActivity getMetaDataActivity,
            UpdateExportDataActivity updateExportDataActivity,
            HandleFailureActivity handleFailureActivity)
        {
            this.uploadActivity = uploadActivity;
            this.sendFileCardActivity = sendFileCardActivity;
            this.getMetaDataActivity = getMetaDataActivity;
            this.updateExportDataActivity = updateExportDataActivity;
            this.handleFailureActivity = handleFailureActivity;
        }

        /// <summary>
        /// This is the durable orchestration method,
        /// which starts the export process.
        /// </summary>
        /// <param name="context">Durable orchestration context.</param>
        /// <param name="log">Logging service.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [FunctionName(nameof(ExportOrchestrationAsync))]
        public async Task ExportOrchestrationAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            var exportRequiredData = context.GetInput<ExportDataRequirement>();
            var sentNotificationDataEntity = exportRequiredData.NotificationDataEntity;
            var exportDataEntity = exportRequiredData.ExportDataEntity;

            if (!context.IsReplaying)
            {
                log.LogInformation($"Start to export the notification {sentNotificationDataEntity.Id}!");
            }

            try
            {
                // Update the status of export as in progress.
                exportDataEntity.Status = ExportStatus.InProgress.ToString();
                await this.updateExportDataActivity.RunAsync(context, exportDataEntity, log);

                var metaData = await this.getMetaDataActivity.RunAsync(context, (sentNotificationDataEntity, exportDataEntity), log);
                await this.uploadActivity.RunAsync(context, (sentNotificationDataEntity, metaData, exportDataEntity.FileName), log);
                var consentId = await this.sendFileCardActivity.RunAsync(context, (exportRequiredData.UserId, exportRequiredData.NotificationDataEntity.Id, exportDataEntity.FileName), log);

                // Update export as completed.
                exportDataEntity.FileConsentId = consentId;
                exportDataEntity.Status = ExportStatus.Completed.ToString();
                await this.updateExportDataActivity.RunAsync(context, exportDataEntity, log);

                log.LogInformation($"Export Notification Successful!");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to export notification {sentNotificationDataEntity.Id} : {ex.Message}";
                log.LogError(ex, errorMessage);

                await this.handleFailureActivity.RunAsync(context, exportDataEntity, log);
            }
        }
    }
}