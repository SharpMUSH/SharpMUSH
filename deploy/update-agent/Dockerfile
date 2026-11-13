# The update agent: Python and the Docker CLI with its Compose plugin, nothing else.
FROM docker.io/library/python:3.13-alpine
RUN apk add --no-cache docker-cli docker-cli-compose
COPY agent.py /opt/update-agent/agent.py
ENTRYPOINT ["python3", "/opt/update-agent/agent.py"]
