aoi-ops-platform/
  frontend/
    src/
    public/
    package.json
    vite.config.ts

  backend/
    src/
      Api/
      Application/
      Domain/
      Infrastructure/
        Mqtt/          ← 新增：MQTT subscriber 實作
        TimeSeries/    ← 新增：InfluxDB 寫入邏輯
    tests/
    AOIOpsPlatform.sln

  services/
    data-simulator/
      app/
        mqtt_publisher.py   ← 核心：模擬設備透過 MQTT 發數據
        scenario/           ← 正常 / 異常 / 漂移 / 誤判情境
      requirements.txt
      Dockerfile
    vision-helper/
      app/
      requirements.txt
      Dockerfile
    ai-copilot/
      app/
      requirements.txt
      Dockerfile

  infra/
    docker/
      docker-compose.yml    ← 包含 Mosquitto、InfluxDB
    db/
      init/
      migrations/
    mqtt/
      mosquitto.conf        ← Broker 設定

  docs/
    architecture.md
    erd.md
    api-spec.md
    product-scope.md
    mqtt-flow.md            ← 新增：設備數據流說明

  scripts/
    dev/
    seed/

  README.md