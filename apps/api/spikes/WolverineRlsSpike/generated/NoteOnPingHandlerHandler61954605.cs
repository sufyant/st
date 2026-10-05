    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class NoteOnPingHandlerHandler61954605 : Wolverine.Runtime.Handlers.MessageHandler
    {
        private readonly Microsoft.EntityFrameworkCore.DbContextOptions<WolverineRlsSpike.SpikeDbContext> _dbContextOptionsOfSpikeDbContext;
        private readonly System.Collections.Generic.IEnumerable<Wolverine.EntityFrameworkCore.IDomainEventScraper> _domainEventScraperIEnumerable;

        public NoteOnPingHandlerHandler61954605(Microsoft.EntityFrameworkCore.DbContextOptions<WolverineRlsSpike.SpikeDbContext> dbContextOptionsOfSpikeDbContext, System.Collections.Generic.IEnumerable<Wolverine.EntityFrameworkCore.IDomainEventScraper> domainEventScraperIEnumerable)
        {
            _dbContextOptionsOfSpikeDbContext = dbContextOptionsOfSpikeDbContext;
            _domainEventScraperIEnumerable = domainEventScraperIEnumerable;
        }



        public override async System.Threading.Tasks.Task HandleAsync(Wolverine.Runtime.MessageContext context, System.Threading.CancellationToken cancellation)
        {
            await using var spikeDbContext = new WolverineRlsSpike.SpikeDbContext(_dbContextOptionsOfSpikeDbContext, context);
            // The actual message body
            var ping = (WolverineRlsSpike.Ping)context.Envelope.Message;

            
            // Enroll the DbContext & IMessagingContext in the outgoing Wolverine outbox transaction
            var efCoreEnvelopeTransaction = new Wolverine.EntityFrameworkCore.Internals.EfCoreEnvelopeTransaction(spikeDbContext, context, _domainEventScraperIEnumerable);
            await context.EnlistInOutboxAsync(efCoreEnvelopeTransaction).ConfigureAwait(false);
            // Start the actual database transaction if one does not already exist
            if (spikeDbContext.Database.CurrentTransaction == null)
            {
                await spikeDbContext.Database.BeginTransactionAsync(cancellation).ConfigureAwait(false);
            }

            try
            {
                System.Diagnostics.Activity.Current?.SetTag("message.handler", "WolverineRlsSpike.NoteOnPingHandler");
                System.Diagnostics.Activity.Current?.SetTag("handler.type", "WolverineRlsSpike.NoteOnPingHandler");
                
                // The actual message execution
                WolverineRlsSpike.NoteOnPingHandler.Handle(ping, spikeDbContext);

                
                // Added by EF Core Transaction Middleware
                var result_of_SaveChangesAsync = await spikeDbContext.SaveChangesAsync(cancellation).ConfigureAwait(false);

                // Commit the EF Core transaction before writing the response (GH-2917). The outbox flush follows in its own frame (GH-4742)
                await efCoreEnvelopeTransaction.CommitAsync(cancellation, flushOutgoingMessages: false).ConfigureAwait(false);
                // GH-2917/GH-4742: flush the outbox after the commit and before the response is written
                await context.FlushOutgoingMessagesAsync().ConfigureAwait(false);
            }

            catch (System.Exception)
            {
                await efCoreEnvelopeTransaction.RollbackAsync().ConfigureAwait(false);
                throw;
            }

        }

    }

