// <copyright file="UserTeamsActivityHandler.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Bot
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Bot.Builder;
    using Microsoft.Bot.Builder.Teams;
    using Microsoft.Bot.Schema;
    using Microsoft.Bot.Schema.Teams;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.NotificationData;
    using Microsoft.ApplicationInsights;
    using System.Collections.Generic;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.SentNotificationData;
    using System.Linq;

    /// <summary>
    /// Company Communicator User Bot.
    /// Captures user data, team data.
    /// </summary>
    public class UserTeamsActivityHandler : TeamsActivityHandler
    {
        private TelemetryClient telemetry = new TelemetryClient();

        private static readonly string TeamRenamedEventType = "teamRenamed";

        private readonly TeamsDataCapture teamsDataCapture;
        private readonly ISentNotificationDataRepository sentNotificationDataRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserTeamsActivityHandler"/> class.
        /// </summary>
        /// <param name="teamsDataCapture">Teams data capture service.</param>
        /// <param name="sentNotificationDataRepository">Sent notification data.</param>
        public UserTeamsActivityHandler(TeamsDataCapture teamsDataCapture, ISentNotificationDataRepository sentNotificationDataRepository)
        {
            this.teamsDataCapture = teamsDataCapture ?? throw new ArgumentNullException(nameof(teamsDataCapture));
            this.sentNotificationDataRepository = sentNotificationDataRepository ?? throw new ArgumentNullException(nameof(sentNotificationDataRepository));
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            string autoReplyMessage = "Thank you for your message. Please reach out to <a href='mailto:feedback@hearst.com'>feedback@hearst.com</a> with any questions.";
            await turnContext.SendActivityAsync(autoReplyMessage);
        }

        protected override async Task OnReactionsAddedAsync(IList<MessageReaction> messageReactions, ITurnContext<IMessageReactionActivity> turnContext, CancellationToken cancellationToken)
        {
            string userId = turnContext.Activity.From.AadObjectId;
            string messageId = turnContext.Activity.ReplyToId;
            string filter = $"RowKey eq '{userId}' and MessageId eq '{messageId}'";

            try
            {
                var result = await this.sentNotificationDataRepository.GetWithFilterAsync(filter);
                var entity = result.First();
                entity.MessageReaction = 1;
                await this.sentNotificationDataRepository.InsertOrMergeAsync(entity);
            }
            catch (Exception ex)
            {

                await turnContext.SendActivityAsync($"failed to interact with the notification repo, {ex.Message}");
            }

            Dictionary<string, string> telemetryProperties = new Dictionary<string, string>();
            telemetryProperties.Add("Filter", filter);
            telemetryProperties.Add("From.AAD object", turnContext.Activity.From.AadObjectId);
            telemetryProperties.Add("From.Id", turnContext.Activity.From.Id);
            telemetryProperties.Add("# of Reaction Added", turnContext.Activity.ReactionsAdded.Count.ToString());
            telemetryProperties.Add("# of Reaction Removed", turnContext.Activity.ReactionsRemoved.Count.ToString());
            telemetryProperties.Add("ReplyToId", messageId);
            this.telemetry.TrackEvent("MessageReaction", telemetryProperties);
            await base.OnReactionsAddedAsync(messageReactions, turnContext, cancellationToken);
        }

        /// <summary>
        /// Invoked when a conversation update activity is received from the channel.
        /// </summary>
        /// <param name="turnContext">The context object for this turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        protected override async Task OnConversationUpdateActivityAsync(
            ITurnContext<IConversationUpdateActivity> turnContext,
            CancellationToken cancellationToken)
        {
            // base.OnConversationUpdateActivityAsync is useful when it comes to responding to users being added to or removed from the conversation.
            // For example, a bot could respond to a user being added by greeting the user.
            // By default, base.OnConversationUpdateActivityAsync will call <see cref="OnMembersAddedAsync(IList{ChannelAccount}, ITurnContext{IConversationUpdateActivity}, CancellationToken)"/>
            // if any users have been added or <see cref="OnMembersRemovedAsync(IList{ChannelAccount}, ITurnContext{IConversationUpdateActivity}, CancellationToken)"/>
            // if any users have been removed. base.OnConversationUpdateActivityAsync checks the member ID so that it only responds to updates regarding members other than the bot itself.
            await base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);

            var activity = turnContext.Activity;

            var isTeamRenamed = this.IsTeamInformationUpdated(activity);
            if (isTeamRenamed)
            {
                await this.teamsDataCapture.OnTeamInformationUpdatedAsync(activity);
            }

            if (activity.MembersAdded != null)
            {
                await this.teamsDataCapture.OnBotAddedAsync(turnContext, activity, cancellationToken);
            }

            if (activity.MembersRemoved != null)
            {
                await this.teamsDataCapture.OnBotRemovedAsync(activity);
            }
        }

        private bool IsTeamInformationUpdated(IConversationUpdateActivity activity)
        {
            if (activity == null)
            {
                return false;
            }

            var channelData = activity.GetChannelData<TeamsChannelData>();
            if (channelData == null)
            {
                return false;
            }

            return UserTeamsActivityHandler.TeamRenamedEventType.Equals(channelData.EventType, StringComparison.OrdinalIgnoreCase);
        }
    }
}