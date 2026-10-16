FROM docker.io/mazzolino/restic:1.8
USER root
RUN apk add --no-cache python3
COPY agent.py /opt/backup-agent/agent.py
ENTRYPOINT ["python3", "/opt/backup-agent/agent.py"]
