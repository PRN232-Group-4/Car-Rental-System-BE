# Local development startup configuration

- Added the WebApi development configuration with the non-secret local Redis endpoint (`localhost:6379`).
- Development now defaults to that endpoint when neither `Redis__ConnectionString` nor `REDIS_URL` is supplied; production still requires an explicit configuration.
- Development also defaults to the Docker Compose PostgreSQL endpoint (`localhost:5432`, database `car_rental`, user/password `postgres`) when no database connection string is supplied. This allows DI validation and Swagger to start; database-backed endpoints require PostgreSQL to be running.
- Brevo settings are validated when an OTP is sent, so an unconfigured local email service no longer blocks Swagger/API startup. OTP requests return a clear 503 until Brevo is configured.
