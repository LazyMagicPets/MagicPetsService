namespace LambdaFunc;
public partial class Startup
{
    //public const string AppS3BucketKey = "AppS3Bucket";

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public static IConfiguration Configuration { get; private set; }

    // This method gets called by the runtime. Use this method to add services to the container
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDefaultAWSOptions(Configuration.GetAWSOptions());
        ConfigureSvcs(services);
        services.AddControllers().AddNewtonsoftJson();
        // Add S3 to the ASP.NET Core dependency injection framework.
        // services.AddAWSService<Amazon.S3.IAmazonS3>();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
