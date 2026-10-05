    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class StartAndCascadeHandler2067440659 : Wolverine.Runtime.Handlers.MessageHandler
    {
        private readonly Microsoft.EntityFrameworkCore.DbContextOptions<WolverineRlsSpike.SpikeDbContext> _dbContextOptionsOfSpikeDbContext;
        private readonly System.Collections.Generic.IEnumerable<Wolverine.EntityFrameworkCore.IDomainEventScraper> _domainEventScraperIEnumerable;

        public StartAndCascadeHandler2067440659(Microsoft.EntityFrameworkCore.DbContextOptions<WolverineRlsSpike.SpikeDbContext> dbContextOptionsOfSpikeDbContext, System.Collections.Generic.IEnumerable<Wolverine.EntityFrameworkCore.IDomainEventScraper> domainEventScraperIEnumerable)
        {
            _dbContextOptionsOfSpikeDbContext = dbContextOptionsOfSpikeDbContext;
            _domainEventScraperIEnumerable = domainEventScraperIEnumerable;
        }



        public override async System.Threading.Tasks.Task HandleAsync(Wolverine.Runtime.MessageContext context, System.Threading.CancellationToken cancellation)
        {
            await using var spikeDbContext = new WolverineRlsSpike.SpikeDbContext(_dbContextOptionsOfSpikeDbContext, context);
            // The actual message body
            var startAndCascade = (WolverineRlsSpike.StartAndCascade)context.Envelope.Message;

            // Application-specific Open Telemetry auditing
            System.Diagnostics.Activity.Current?.SetTag("SpikeSagaId", startAndCascade.SpikeSagaId);
            
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
                
                // The actual message execution
                (var outgoing1, var outgoing2) = WolverineRlsSpike.SpikeSaga.Start(startAndCascade);

                context.SetSagaId(startAndCascade.SpikeSagaId);
                System.Diagnostics.Activity.Current?.SetTag("wolverine.saga.id", startAndCascade.SpikeSagaId.ToString());
                System.Diagnostics.Activity.Current?.SetTag("wolverine.saga.type", "WolverineRlsSpike.SpikeSaga");
                
                // Outgoing, cascaded message
                await context.EnqueueCascadingAsync(outgoing2).ConfigureAwait(false);

                if (!outgoing1.IsCompleted())
                {
                    
                    // Registering the Saga entity change
                    spikeDbContext.Add(outgoing1);
                }

                try
                {
                    
                    // Committing any pending entity changes to the database
                    var result_of_SaveChangesAsync1 = await spikeDbContext.SaveChangesAsync(cancellation).ConfigureAwait(false);

                }

                catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException error)
                {
                    // Only intercepts concurrency error on the saga itself
                    if (System.Linq.Enumerable.Any(error.Entries, e => e.Entity == outgoing1))
                    {
                        throw new Wolverine.SagaConcurrencyException($"Saga of type WolverineRlsSpike.SpikeSaga and identity sagaId cannot be updated because of optimistic concurrency violations");
                    }

                    // Rethrow any other exception
                    throw;
                }

                
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

