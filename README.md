# DataProcessorService

This project demonstrates a background service architecture using **.NET Worker Services**, **RabbitMQ**, and **SQLite**.
The system does not expose REST API endpoints — instead it works with background tasks and message queues.

---

## 📌 Overview

The solution contains the following components:

1. **FilePollingService**
   Watches a folder for new files. When a file is detected, it is published as a message to RabbitMQ.

2. **RabbitConsumerService**
   Subscribes to a RabbitMQ queue. Processes received messages and stores results into a SQLite database.

3. **RabbitMQ (via Docker Desktop)**
   Acts as the message broker, decoupling the producer (FilePollingService) from the consumer (RabbitConsumerService).

---

## 🛠 Prerequisites

* [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
* [.NET 6/7 SDK](https://dotnet.microsoft.com/download) installed

---

## ▶️ Running RabbitMQ with Docker

1. Pull the RabbitMQ image with the management UI:

```sh
docker pull rabbitmq:3-management
```

2. Run RabbitMQ in a container:

```sh
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

3. Verify it is running:

```sh
docker ps
```

You should see `rabbitmq` container with ports `5672` (broker) and `15672` (management UI).

4. Open the management UI in a browser:
   👉 [http://localhost:15672](http://localhost:15672)
   Default login: `guest / guest`

---

## ▶️ Running the Project

1. Clone the repository:

```sh
git clone https://github.com/<your-repo>/DataProcessorService.git
cd DataProcessorService
```

2. Restore dependencies:

```sh
dotnet restore
```

3. Run the service:

```sh
dotnet run
```

---

## 🔍 How It Works

* **FilePollingService** detects new files in the watch folder.
* Publishes a message to RabbitMQ (`instrument_exchange` with routing key `instrument.status`).
* **RabbitConsumerService** listens to the `instrument_queue` and processes messages.
* Processed results are stored in the SQLite database (`instrument.db`).

---

## ✅ Testing

### Option 1: Through File Processing

1. Place a test file into the folder being watched by `FilePollingService`.
2. Observe logs — the service should detect the file, publish a message, and the consumer should log its processing.
3. Check the SQLite file (`instrument.db`) for processed entries.

### Option 2: Through RabbitMQ UI

1. Go to [http://localhost:15672](http://localhost:15672).
2. Navigate to **Queues → instrument_queue**.
3. Publish a test message manually.
4. Observe that the consumer service picks it up and logs the message.

---

## 📂 Configuration

All configuration is in `appsettings.json`:

```json
"RabbitMQ": {
  "Host": "localhost",
  "Port": 5672,
  "Username": "guest",
  "Password": "guest",
  "Exchange": "instrument_exchange",
  "RoutingKey": "instrument.status",
  "Queue": "instrument_queue"
},
"SQLite": {
  "Path": "instrument.db"
}
```

---

## 🚀 Notes

* No REST API endpoints are exposed — everything runs as **background tasks**.
* Ensure Docker Desktop is running before starting the project.
* RabbitMQ container can be stopped/started with:

```sh
docker stop rabbitmq
docker start rabbitmq
```

---
