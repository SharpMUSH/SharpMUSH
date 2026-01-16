#!/bin/bash
# Cleanup script for SharpMUSH test containers with reuse enabled

set -e

echo "🧹 Cleaning up SharpMUSH test containers..."

# Function to clean containers by label
cleanup_by_label() {
    local label=$1
    local name=$2
    local containers=$(docker ps -a --filter "label=testcontainers.reuse.hash=${label}" -q)
    
    if [ -n "$containers" ]; then
        echo "  Removing ${name} containers..."
        echo "$containers" | xargs docker rm -f >/dev/null 2>&1
        echo "  ✓ ${name} containers removed"
    else
        echo "  ✓ No ${name} containers found"
    fi
}

# Clean each container type
cleanup_by_label "sharpmush-arango-test" "ArangoDB"
cleanup_by_label "sharpmush-mysql-test" "MySQL"
cleanup_by_label "sharpmush-prometheus-test" "Prometheus"
cleanup_by_label "sharpmush-redis-test" "Redis"
cleanup_by_label "sharpmush-redpanda-test" "RedPanda"

# Verify cleanup
remaining=$(docker ps -a --filter "label=testcontainers.reuse.hash" | grep sharpmush | wc -l)

if [ "$remaining" -eq 0 ]; then
    echo ""
    echo "✅ All SharpMUSH test containers cleaned up successfully!"
else
    echo ""
    echo "⚠️  Warning: ${remaining} containers still running"
    docker ps -a --filter "label=testcontainers.reuse.hash" | grep sharpmush
fi
